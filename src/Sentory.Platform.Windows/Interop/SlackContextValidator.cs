using System.Security.Cryptography;
using System.Text;

namespace Sentory.Platform.Windows.Interop;

public sealed record ValidatedSlackContext(
    Guid EventId,
    nint MainWindow,
    nint RendererWindow,
    uint ProcessId,
    uint ClipboardSequenceNumber,
    DateTimeOffset OccurredAt,
    string ContextHash);

public sealed record SlackDropTarget(
    nint MainWindow,
    nint RendererWindow,
    uint ProcessId,
    WindowBounds Bounds);

public sealed class SlackContextValidator(
    INativeWindowApi native,
    IDiscordWindowApi chromiumWindows)
{
    public const string SlackProcessName = "Slack";
    public const string MainWindowClassName = "Chrome_WidgetWin_1";
    public const string RendererClassName = "Chrome_RenderWidgetHostHWND";

    public bool TryValidate(
        PasteTrigger trigger,
        out ValidatedSlackContext context)
    {
        context = null!;
        if (trigger.ForegroundWindow == nint.Zero ||
            trigger.ForegroundProcessId == 0 ||
            !string.Equals(
                native.GetProcessName(trigger.ForegroundProcessId),
                SlackProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var mainWindow = native.GetRootWindow(trigger.ForegroundWindow);
        if (mainWindow == nint.Zero ||
            native.GetProcessId(mainWindow) != trigger.ForegroundProcessId ||
            !string.Equals(
                native.GetClassName(mainWindow),
                MainWindowClassName,
                StringComparison.Ordinal))
        {
            return false;
        }

        var rendererWindow = chromiumWindows.FindDescendant(
            mainWindow,
            RendererClassName);
        if (rendererWindow == nint.Zero ||
            native.GetProcessId(rendererWindow) != trigger.ForegroundProcessId ||
            native.GetRootWindow(rendererWindow) != mainWindow)
        {
            return false;
        }

        context = new ValidatedSlackContext(
            trigger.EventId,
            mainWindow,
            rendererWindow,
            trigger.ForegroundProcessId,
            trigger.ClipboardSequenceNumber,
            trigger.OccurredAt,
            CreateContextHash(trigger.ForegroundProcessId, mainWindow));
        return true;
    }

    public bool TryValidate(
        SlackDropTarget target,
        uint clipboardSequenceNumber,
        DateTimeOffset occurredAt,
        out ValidatedSlackContext context) =>
        TryValidate(
            new PasteTrigger(
                Guid.NewGuid(),
                target.MainWindow,
                target.RendererWindow,
                target.ProcessId,
                clipboardSequenceNumber,
                occurredAt,
                false),
            out context);

    private static string CreateContextHash(uint processId, nint mainWindow)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"Slack|{processId}|{mainWindow.ToInt64():X}"));
        return Convert.ToHexString(bytes);
    }
}
