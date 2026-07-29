using System.Security.Cryptography;
using System.Text;

namespace Sentory.Platform.Windows.Interop;

public sealed record ValidatedWeChatContext(
    Guid EventId,
    nint MainWindow,
    uint ProcessId,
    uint ClipboardSequenceNumber,
    DateTimeOffset OccurredAt,
    string ContextHash);

public sealed record WeChatDropTarget(
    nint MainWindow,
    uint ProcessId,
    WindowBounds Bounds);

public sealed class WeChatContextValidator(INativeWindowApi native)
{
    private static readonly string[] ProcessNames = ["Weixin", "WeChat"];
    private const string DropSurfaceProcessName = "WeChatAppEx";

    public static bool IsSupportedProcessName(string? processName) =>
        processName is not null &&
        ProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupportedDropSurfaceProcessName(string? processName) =>
        IsSupportedProcessName(processName) ||
        string.Equals(
            processName,
            DropSurfaceProcessName,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedMainWindowClass(string className)
    {
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
        PasteTrigger trigger,
        out ValidatedWeChatContext context)
    {
        context = null!;
        if (trigger.ForegroundWindow == nint.Zero ||
            trigger.ForegroundProcessId == 0 ||
            !IsSupportedProcessName(
                native.GetProcessName(trigger.ForegroundProcessId)))
        {
            return false;
        }

        var foregroundRoot = native.GetRootWindow(trigger.ForegroundWindow);
        if (foregroundRoot == nint.Zero ||
            native.GetProcessId(foregroundRoot) != trigger.ForegroundProcessId)
        {
            return false;
        }

        var mainWindow = ResolveMainWindow(
            foregroundRoot,
            trigger.ForegroundProcessId);
        if (mainWindow == nint.Zero)
        {
            return false;
        }

        context = new ValidatedWeChatContext(
            trigger.EventId,
            mainWindow,
            trigger.ForegroundProcessId,
            trigger.ClipboardSequenceNumber,
            trigger.OccurredAt,
            CreateContextHash(trigger.ForegroundProcessId, mainWindow));
        return true;
    }

    private nint ResolveMainWindow(nint foregroundRoot, uint processId)
    {
        var current = foregroundRoot;
        for (var depth = 0; depth < 5; depth++)
        {
            if (IsSupportedMainWindowClass(native.GetClassName(current)))
            {
                return current;
            }

            var owner = native.GetOwnerWindow(current);
            if (owner == nint.Zero)
            {
                return nint.Zero;
            }

            var ownerRoot = native.GetRootWindow(owner);
            if (ownerRoot == nint.Zero ||
                ownerRoot == current ||
                native.GetProcessId(ownerRoot) != processId)
            {
                return nint.Zero;
            }

            current = ownerRoot;
        }

        return nint.Zero;
    }

    public bool TryValidate(
        WeChatDropTarget target,
        uint clipboardSequenceNumber,
        DateTimeOffset occurredAt,
        out ValidatedWeChatContext context) =>
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

    private static string CreateContextHash(uint processId, nint mainWindow)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"WeChat|{processId}|{mainWindow.ToInt64():X}"));
        return Convert.ToHexString(bytes);
    }
}
