using System.Security.Cryptography;
using System.Text;

namespace Sentory.Platform.Windows.Interop;

public sealed record ValidatedTelegramContext(
    Guid EventId,
    nint MainWindow,
    uint ProcessId,
    uint ClipboardSequenceNumber,
    DateTimeOffset OccurredAt,
    string ContextHash);

public sealed record TelegramDropTarget(
    nint MainWindow,
    uint ProcessId,
    WindowBounds Bounds);

public sealed class TelegramContextValidator(INativeWindowApi native)
{
    public const string ProcessName = "Telegram";

    public static bool IsSupportedMainWindowClass(string className) =>
        className.StartsWith("Qt", StringComparison.Ordinal) &&
        className.EndsWith("QWindowIcon", StringComparison.Ordinal);

    public bool TryValidate(
        PasteTrigger trigger,
        out ValidatedTelegramContext context)
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
            !IsSupportedMainWindowClass(native.GetClassName(foregroundRoot)))
        {
            return false;
        }

        var mainWindow = ResolveOwnedMainWindow(
            foregroundRoot,
            trigger.ForegroundProcessId);

        context = new ValidatedTelegramContext(
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

    public bool TryValidate(
        TelegramDropTarget target,
        uint clipboardSequenceNumber,
        DateTimeOffset occurredAt,
        out ValidatedTelegramContext context) =>
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
                $"Telegram|{processId}|{mainWindow.ToInt64():X}"));
        return Convert.ToHexString(bytes);
    }
}

internal static class TelegramSendButtonPolicy
{
    public static bool IsWithin(
        WindowBounds bounds,
        int screenX,
        int screenY) =>
        bounds.Width >= 320 &&
        bounds.Height >= 320 &&
        screenX >= bounds.Left + bounds.Width * 0.62 &&
        screenY >= bounds.Top + bounds.Height * 0.78 &&
        screenX < bounds.Right &&
        screenY < bounds.Bottom;
}
