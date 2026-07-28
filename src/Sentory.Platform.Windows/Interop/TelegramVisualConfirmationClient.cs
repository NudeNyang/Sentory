using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Sentory.Platform.Windows.Interop;

internal sealed record TelegramVisualFrame(
    WindowBounds Bounds,
    IReadOnlyList<int> ConversationPixels);

internal sealed record TelegramVisualSnapshot(
    TelegramVisualFrame Baseline);

internal sealed record TelegramVisualConfirmationRequest(
    ValidatedTelegramContext Context,
    TelegramVisualSnapshot Snapshot,
    TimeSpan Timeout);

internal sealed record TelegramVisualConfirmationResponse(
    bool Confirmed,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<string> Signals);

internal interface ITelegramVisualFrameSource
{
    TelegramVisualFrame? TryCapture(
        ValidatedTelegramContext context,
        bool requireForeground);
}

internal interface ITelegramVisualConfirmationClient
{
    Task<TelegramVisualSnapshot?> TryCaptureAsync(
        ValidatedTelegramContext context,
        bool requireForeground,
        CancellationToken cancellationToken);

    Task<TelegramVisualConfirmationResponse> WaitForConfirmationAsync(
        TelegramVisualConfirmationRequest request,
        Func<bool> explicitSendObserved,
        CancellationToken cancellationToken);
}

internal sealed class TelegramVisualConfirmationState
{
    internal const double ConversationChangedThreshold = 0.004;

    private readonly TelegramVisualFrame _baseline;
    private TelegramVisualFrame _beforeSend;
    private bool _sendObserved;
    private int _confirmationFrames;

    public TelegramVisualConfirmationState(TelegramVisualFrame baseline)
    {
        _baseline = baseline;
        _beforeSend = baseline;
    }

    public TelegramVisualDecision Observe(
        TelegramVisualFrame current,
        bool explicitSendObserved)
    {
        if (!SameBounds(_baseline.Bounds, current.Bounds))
        {
            return TelegramVisualDecision.Pending;
        }

        if (!explicitSendObserved)
        {
            _beforeSend = current;
            _sendObserved = false;
            _confirmationFrames = 0;
            return TelegramVisualDecision.Pending;
        }

        _sendObserved = true;
        var changed = WhatsAppVisualDifference.Calculate(
            _beforeSend.ConversationPixels,
            current.ConversationPixels) >= ConversationChangedThreshold;
        if (!changed)
        {
            _confirmationFrames = 0;
            return TelegramVisualDecision.Pending;
        }

        _confirmationFrames++;
        return _confirmationFrames >= 2
            ? TelegramVisualDecision.Confirmed
            : TelegramVisualDecision.Pending;
    }

    public bool SendObserved => _sendObserved;

    private static bool SameBounds(WindowBounds left, WindowBounds right) =>
        left.Width == right.Width && left.Height == right.Height;
}

internal enum TelegramVisualDecision
{
    Pending,
    Confirmed
}

internal sealed class TelegramScreenFrameSource(
    INativeWindowApi native) : ITelegramVisualFrameSource
{
    private const int SampleWidth = 96;
    private const int SampleHeight = 72;

    public TelegramVisualFrame? TryCapture(
        ValidatedTelegramContext context,
        bool requireForeground)
    {
        if (requireForeground &&
            native.GetRootWindow(native.GetForegroundWindow()) !=
            context.MainWindow)
        {
            return null;
        }

        var bounds = native.GetWindowBounds(context.MainWindow);
        if (bounds.Width < 320 || bounds.Height < 320)
        {
            return null;
        }

        try
        {
            using var source = new Bitmap(
                bounds.Width,
                bounds.Height,
                PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(source))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    source.Size,
                    CopyPixelOperation.SourceCopy);
            }

            var region = RelativeRegion(
                bounds,
                left: 0.24,
                top: 0.14,
                right: 0.995,
                bottom: 0.94);
            return new TelegramVisualFrame(
                bounds,
                Sample(source, region, SampleWidth, SampleHeight));
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  ExternalException or
                  InvalidOperationException)
        {
            return null;
        }
    }

    private static Rectangle RelativeRegion(
        WindowBounds bounds,
        double left,
        double top,
        double right,
        double bottom)
    {
        var x = (int)Math.Round(bounds.Width * left);
        var y = (int)Math.Round(bounds.Height * top);
        var width = Math.Max(1, (int)Math.Round(bounds.Width * right) - x);
        var height = Math.Max(1, (int)Math.Round(bounds.Height * bottom) - y);
        return new Rectangle(x, y, width, height);
    }

    private static IReadOnlyList<int> Sample(
        Bitmap source,
        Rectangle region,
        int width,
        int height)
    {
        using var reduced = new Bitmap(
            width,
            height,
            PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(reduced))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                region,
                GraphicsUnit.Pixel);
        }

        var pixels = new int[width * height];
        var position = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[position++] = reduced.GetPixel(x, y).ToArgb();
            }
        }

        return pixels;
    }
}

internal sealed class TelegramVisualConfirmationClient(
    ITelegramVisualFrameSource source,
    Action<string, string>? diagnostic = null) :
    ITelegramVisualConfirmationClient
{
    public Task<TelegramVisualSnapshot?> TryCaptureAsync(
        ValidatedTelegramContext context,
        bool requireForeground,
        CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = source.TryCapture(context, requireForeground);
                if (frame is null)
                {
                    diagnostic?.Invoke(
                        "telegram-context-rejected",
                        "reason=visual-baseline-unavailable");
                    return null;
                }

                diagnostic?.Invoke(
                    "telegram-context-ready",
                    $"width={frame.Bounds.Width} height={frame.Bounds.Height}");
                return new TelegramVisualSnapshot(frame);
            },
            cancellationToken);

    public async Task<TelegramVisualConfirmationResponse>
        WaitForConfirmationAsync(
            TelegramVisualConfirmationRequest request,
            Func<bool> explicitSendObserved,
            CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var state = new TelegramVisualConfirmationState(
            request.Snapshot.Baseline);
        while (DateTimeOffset.UtcNow - startedAt < request.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = source.TryCapture(
                request.Context,
                requireForeground: true);
            if (frame is not null &&
                state.Observe(frame, explicitSendObserved()) ==
                TelegramVisualDecision.Confirmed)
            {
                diagnostic?.Invoke(
                    "telegram-send-confirmed",
                    "sendInput=True visualChange=True");
                return new TelegramVisualConfirmationResponse(
                    true,
                    DateTimeOffset.UtcNow,
                    [
                        "telegram-explicit-send-input",
                        "telegram-conversation-region-changed"
                    ]);
            }

            await Task.Delay(120, cancellationToken);
        }

        diagnostic?.Invoke(
            "telegram-candidate-expired",
            $"sendObserved={state.SendObserved}");
        return new TelegramVisualConfirmationResponse(
            false,
            null,
            ["telegram-confirmation-timeout"]);
    }
}
