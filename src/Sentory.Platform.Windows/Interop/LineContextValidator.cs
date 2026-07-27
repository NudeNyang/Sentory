using System.Security.Cryptography;
using System.Text;

namespace Sentory.Platform.Windows.Interop;

public sealed record ValidatedLineContext(
    Guid EventId,
    nint MainWindow,
    uint ProcessId,
    uint ClipboardSequenceNumber,
    DateTimeOffset OccurredAt,
    string ContextHash);

public sealed record LineDropTarget(
    nint MainWindow,
    uint ProcessId,
    WindowBounds Bounds);

public sealed class LineContextValidator(INativeWindowApi native)
{
    public const string ProcessName = "LINE";
    public const string MainWindowClassName = "AllInOneWindow";

    public bool TryValidate(
        PasteTrigger trigger,
        out ValidatedLineContext context)
    {
        context = null!;
        if (trigger.ForegroundWindow == nint.Zero ||
            trigger.ForegroundProcessId == 0 ||
            !string.Equals(
                native.GetProcessName(trigger.ForegroundProcessId),
                ProcessName,
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

        context = new ValidatedLineContext(
            trigger.EventId,
            mainWindow,
            trigger.ForegroundProcessId,
            trigger.ClipboardSequenceNumber,
            trigger.OccurredAt,
            CreateContextHash(trigger.ForegroundProcessId, mainWindow));
        return true;
    }

    public bool TryValidate(
        LineDropTarget target,
        uint clipboardSequenceNumber,
        DateTimeOffset occurredAt,
        out ValidatedLineContext context) =>
        TryValidate(
            new PasteTrigger(
                Guid.NewGuid(),
                target.MainWindow,
                target.MainWindow,
                target.ProcessId,
                clipboardSequenceNumber,
                occurredAt,
                false),
            out context);

    private static string CreateContextHash(
        uint processId,
        nint mainWindow)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"LINE|{processId}|{mainWindow.ToInt64():X}"));
        return Convert.ToHexString(bytes);
    }
}
