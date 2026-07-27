using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Sentory.Platform.Windows.Interop;

internal sealed record WhatsAppVisualFrame(
    WindowBounds Bounds,
    IReadOnlyList<int> DraftPixels,
    IReadOnlyList<int> OutgoingPixels,
    bool HasSendAffordance);

internal sealed record WhatsAppVisualSnapshot(
    WhatsAppVisualFrame Baseline);

internal sealed record WhatsAppVisualConfirmationRequest(
    ValidatedWhatsAppContext Context,
    WhatsAppVisualSnapshot Snapshot,
    TimeSpan Timeout);

internal sealed record WhatsAppVisualConfirmationResponse(
    bool Confirmed,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<string> Signals);

internal enum WhatsAppVisualDecision
{
    Pending,
    Confirmed,
    Cancelled
}

internal interface IWhatsAppVisualFrameSource
{
    WhatsAppVisualFrame? TryCapture(
        ValidatedWhatsAppContext context,
        bool requireForeground);
}

internal interface IWhatsAppVisualConfirmationClient
{
    Task<WhatsAppVisualSnapshot?> TryCaptureAsync(
        ValidatedWhatsAppContext context,
        bool requireForeground,
        CancellationToken cancellationToken);

    Task<WhatsAppVisualConfirmationResponse> WaitForConfirmationAsync(
        WhatsAppVisualConfirmationRequest request,
        Func<bool> explicitSendObserved,
        CancellationToken cancellationToken);
}

internal static class WhatsAppVisualDifference
{
    public static double Calculate(
        IReadOnlyList<int> left,
        IReadOnlyList<int> right)
    {
        if (left.Count == 0 || left.Count != right.Count)
        {
            return 1;
        }

        var changed = 0;
        for (var index = 0; index < left.Count; index++)
        {
            var first = left[index];
            var second = right[index];
            var distance =
                Math.Abs(((first >> 16) & 0xFF) - ((second >> 16) & 0xFF)) +
                Math.Abs(((first >> 8) & 0xFF) - ((second >> 8) & 0xFF)) +
                Math.Abs((first & 0xFF) - (second & 0xFF));
            if (distance >= 54)
            {
                changed++;
            }
        }

        return changed / (double)left.Count;
    }
}

internal sealed class WhatsAppVisualConfirmationState(
    WhatsAppVisualFrame baseline,
    TimeSpan cancellationGracePeriod)
{
    internal const double OutgoingChangedThreshold = 0.006;

    private WhatsAppVisualFrame? _draft;
    private DateTimeOffset? _missingSince;
    private int _confirmationFrames;

    public bool DraftObserved => _draft is not null;

    public WhatsAppVisualDecision Observe(
        WhatsAppVisualFrame current,
        bool explicitSendObserved,
        DateTimeOffset observedAt)
    {
        if (!SameBounds(baseline.Bounds, current.Bounds))
        {
            return WhatsAppVisualDecision.Pending;
        }

        if (_draft is null)
        {
            if (current.HasSendAffordance)
            {
                _draft = current;
            }

            return WhatsAppVisualDecision.Pending;
        }

        var outgoingChanged = WhatsAppVisualDifference.Calculate(
            _draft.OutgoingPixels,
            current.OutgoingPixels) >= OutgoingChangedThreshold;

        if (!current.HasSendAffordance &&
            explicitSendObserved &&
            outgoingChanged)
        {
            _missingSince = null;
            _confirmationFrames++;
            return _confirmationFrames >= 2
                ? WhatsAppVisualDecision.Confirmed
                : WhatsAppVisualDecision.Pending;
        }

        _confirmationFrames = 0;
        if (current.HasSendAffordance || explicitSendObserved)
        {
            _missingSince = null;
            return WhatsAppVisualDecision.Pending;
        }

        _missingSince ??= observedAt;
        return observedAt - _missingSince >= cancellationGracePeriod
            ? WhatsAppVisualDecision.Cancelled
            : WhatsAppVisualDecision.Pending;
    }

    private static bool SameBounds(WindowBounds left, WindowBounds right) =>
        left.Width == right.Width && left.Height == right.Height;
}

internal sealed class WhatsAppScreenFrameSource(
    INativeWindowApi native) : IWhatsAppVisualFrameSource
{
    private const int DraftWidth = 120;
    private const int DraftHeight = 36;
    private const int OutgoingWidth = 96;
    private const int OutgoingHeight = 72;

    public WhatsAppVisualFrame? TryCapture(
        ValidatedWhatsAppContext context,
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

            var draftRegion = RelativeRegion(
                bounds,
                left: 0.32,
                top: 0.82,
                right: 0.995,
                bottom: 0.995);
            var outgoingRegion = RelativeRegion(
                bounds,
                left: 0.58,
                top: 0.20,
                right: 0.995,
                bottom: 0.82);
            return new WhatsAppVisualFrame(
                bounds,
                Sample(source, draftRegion, DraftWidth, DraftHeight),
                Sample(source, outgoingRegion, OutgoingWidth, OutgoingHeight),
                HasGreenSendAffordance(source, draftRegion));
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

    private static bool HasGreenSendAffordance(
        Bitmap source,
        Rectangle draftRegion)
    {
        var left = draftRegion.Left +
                   (int)Math.Round(draftRegion.Width * 0.86);
        var top = draftRegion.Top +
                  (int)Math.Round(draftRegion.Height * 0.40);
        var right = draftRegion.Right;
        var bottom = draftRegion.Bottom;
        var stepX = Math.Max(1, (right - left) / 40);
        var stepY = Math.Max(1, (bottom - top) / 24);
        var green = 0;
        var sampled = 0;
        for (var y = top; y < bottom; y += stepY)
        {
            for (var x = left; x < right; x += stepX)
            {
                var pixel = source.GetPixel(x, y);
                sampled++;
                if (pixel.G >= 105 &&
                    pixel.G >= pixel.R + 28 &&
                    pixel.G >= pixel.B + 12)
                {
                    green++;
                }
            }
        }

        return sampled > 0 && green / (double)sampled >= 0.012;
    }
}

internal sealed class WhatsAppVisualConfirmationClient(
    IWhatsAppVisualFrameSource source,
    Action<string, string>? diagnostic = null) :
    IWhatsAppVisualConfirmationClient
{
    public Task<WhatsAppVisualSnapshot?> TryCaptureAsync(
        ValidatedWhatsAppContext context,
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
                        "whatsapp-context-rejected",
                        "reason=visual-baseline-unavailable");
                    return null;
                }

                diagnostic?.Invoke(
                    "whatsapp-context-ready",
                    $"width={frame.Bounds.Width} height={frame.Bounds.Height}");
                return new WhatsAppVisualSnapshot(frame);
            },
            cancellationToken);

    public async Task<WhatsAppVisualConfirmationResponse>
        WaitForConfirmationAsync(
            WhatsAppVisualConfirmationRequest request,
            Func<bool> explicitSendObserved,
            CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var state = new WhatsAppVisualConfirmationState(
            request.Snapshot.Baseline,
            TimeSpan.FromSeconds(10));
        var draftLogged = false;
        while (DateTimeOffset.UtcNow - startedAt < request.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = source.TryCapture(
                request.Context,
                requireForeground: true);
            if (frame is not null)
            {
                var decision = state.Observe(
                    frame,
                    explicitSendObserved(),
                    DateTimeOffset.UtcNow);
                if (state.DraftObserved && !draftLogged)
                {
                    draftLogged = true;
                    diagnostic?.Invoke(
                        "whatsapp-draft-state",
                        "present=True");
                }

                if (decision == WhatsAppVisualDecision.Confirmed)
                {
                    diagnostic?.Invoke(
                        "whatsapp-send-confirmed",
                        $"sendKey={explicitSendObserved()}");
                    return new WhatsAppVisualConfirmationResponse(
                        true,
                        DateTimeOffset.UtcNow,
                        [
                            "whatsapp-visual-draft",
                            "whatsapp-draft-removed",
                            "whatsapp-outgoing-region-changed"
                        ]);
                }

                if (decision == WhatsAppVisualDecision.Cancelled)
                {
                    diagnostic?.Invoke(
                        "whatsapp-candidate-cancelled",
                        "reason=draft-removed-without-outgoing-message");
                    return new WhatsAppVisualConfirmationResponse(
                        false,
                        null,
                        ["whatsapp-draft-cancelled"]);
                }
            }

            await Task.Delay(200, cancellationToken);
        }

        diagnostic?.Invoke(
            "whatsapp-candidate-expired",
            $"draftObserved={state.DraftObserved}");
        return new WhatsAppVisualConfirmationResponse(
            false,
            null,
            ["whatsapp-confirmation-timeout"]);
    }
}
