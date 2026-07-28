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

        var foregroundRoot = native.GetRootWindow(trigger.ForegroundWindow);
        if (foregroundRoot == nint.Zero ||
            native.GetProcessId(foregroundRoot) !=
            trigger.ForegroundProcessId ||
            !IsSupportedMainWindowClass(
                native.GetClassName(foregroundRoot)))
        {
            return false;
        }

        var mainWindow = ResolveOwnedMainWindow(
            foregroundRoot,
            trigger.ForegroundProcessId);

        context = new ValidatedLineContext(
            trigger.EventId,
            mainWindow,
            trigger.ForegroundProcessId,
            trigger.ClipboardSequenceNumber,
            trigger.OccurredAt,
            CreateContextHash(trigger.ForegroundProcessId, mainWindow));
        return true;
    }

    private nint ResolveOwnedMainWindow(
        nint foregroundRoot,
        uint processId)
    {
        var current = foregroundRoot;
        for (var depth = 0; depth < 4; depth++)
        {
            var owner = native.GetOwnerWindow(current);
            if (owner == nint.Zero)
            {
                break;
            }

            var ownerRoot = native.GetRootWindow(owner);
            if (ownerRoot == nint.Zero ||
                ownerRoot == current ||
                native.GetProcessId(ownerRoot) != processId ||
                !IsSupportedMainWindowClass(
                    native.GetClassName(ownerRoot)))
            {
                break;
            }

            current = ownerRoot;
        }

        return current;
    }

    public static bool IsSupportedMainWindowClass(string className)
    {
        if (string.Equals(
                className,
                MainWindowClassName,
                StringComparison.Ordinal))
        {
            return true;
        }

        const string qtPrefix = "Qt";
        const string qtSuffix = "QWindowIcon";
        if (!className.StartsWith(qtPrefix, StringComparison.Ordinal) ||
            !className.EndsWith(qtSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var version = className.AsSpan(
            qtPrefix.Length,
            className.Length - qtPrefix.Length - qtSuffix.Length);
        return version.Length > 0 &&
               version.IndexOfAnyExceptInRange('0', '9') < 0;
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
