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

        var mainWindow = native.GetRootWindow(trigger.ForegroundWindow);
        if (mainWindow == nint.Zero ||
            native.GetProcessId(mainWindow) != trigger.ForegroundProcessId ||
            !IsSupportedMainWindowClass(native.GetClassName(mainWindow)))
        {
            return false;
        }

        context = new ValidatedTelegramContext(
            trigger.EventId,
            mainWindow,
            trigger.ForegroundProcessId,
            trigger.ClipboardSequenceNumber,
            trigger.OccurredAt,
            CreateContextHash(trigger.ForegroundProcessId, mainWindow));
        return true;
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
