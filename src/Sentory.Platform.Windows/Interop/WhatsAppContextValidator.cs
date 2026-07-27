using System.Security.Cryptography;
using System.Text;

namespace Sentory.Platform.Windows.Interop;

public sealed record ValidatedWhatsAppContext(
    Guid EventId,
    nint MainWindow,
    nint RendererWindow,
    uint ProcessId,
    uint ClipboardSequenceNumber,
    DateTimeOffset OccurredAt,
    string ContextHash);

public sealed record WhatsAppDropTarget(
    nint MainWindow,
    nint RendererWindow,
    uint ProcessId,
    WindowBounds Bounds);

public sealed class WhatsAppContextValidator(INativeWindowApi native)
{
    public const string ProcessName = "WhatsApp.Root";
    public const string MainWindowClassName =
        "WinUIDesktopWin32WindowClass";
    public const string RendererClassName = "Chrome_WidgetWin_0";

    public bool TryValidate(
        PasteTrigger trigger,
        out ValidatedWhatsAppContext context)
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

        var rendererWindow = trigger.FocusedWindow;
        if (rendererWindow == nint.Zero ||
            native.GetProcessId(rendererWindow) != trigger.ForegroundProcessId ||
            native.GetRootWindow(rendererWindow) != mainWindow ||
            !string.Equals(
                native.GetClassName(rendererWindow),
                RendererClassName,
                StringComparison.Ordinal))
        {
            return false;
        }

        context = new ValidatedWhatsAppContext(
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
        WhatsAppDropTarget target,
        uint clipboardSequenceNumber,
        DateTimeOffset occurredAt,
        out ValidatedWhatsAppContext context) =>
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

    private static string CreateContextHash(
        uint processId,
        nint mainWindow)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"WhatsApp|{processId}|{mainWindow.ToInt64():X}"));
        return Convert.ToHexString(bytes);
    }
}

internal static class WhatsAppSendButtonPolicy
{
    public static bool IsWithin(
        WindowBounds bounds,
        int screenX,
        int screenY) =>
        bounds.Width >= 320 &&
        bounds.Height >= 320 &&
        screenX >= bounds.Left + bounds.Width * 0.88 &&
        screenY >= bounds.Top + bounds.Height * 0.78 &&
        screenX < bounds.Right &&
        screenY < bounds.Bottom;
}
