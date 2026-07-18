using System.Security.Cryptography;
using System.Text;

namespace Sentory.Platform.Windows.Interop;

public sealed record ValidatedDiscordContext(
    Guid EventId,
    nint MainWindow,
    nint RendererWindow,
    uint ProcessId,
    uint ClipboardSequenceNumber,
    DateTimeOffset OccurredAt,
    string ContextHash);

public sealed class DiscordContextValidator(
    INativeWindowApi native,
    IDiscordWindowApi discordWindows)
{
    public const string DiscordProcessName = "Discord";
    public const string MainWindowClassName = "Chrome_WidgetWin_1";
    public const string RendererClassName = "Chrome_RenderWidgetHostHWND";

    public bool TryValidate(
        PasteTrigger trigger,
        out ValidatedDiscordContext context)
    {
        context = null!;
        if (trigger.ForegroundWindow == nint.Zero ||
            trigger.ForegroundProcessId == 0)
        {
            return false;
        }

        if (!string.Equals(
                native.GetProcessName(trigger.ForegroundProcessId),
                DiscordProcessName,
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

        var rendererWindow = discordWindows.FindDescendant(
            mainWindow,
            RendererClassName);
        if (rendererWindow == nint.Zero ||
            native.GetProcessId(rendererWindow) != trigger.ForegroundProcessId ||
            native.GetRootWindow(rendererWindow) != mainWindow)
        {
            return false;
        }

        context = new ValidatedDiscordContext(
            trigger.EventId,
            mainWindow,
            rendererWindow,
            trigger.ForegroundProcessId,
            trigger.ClipboardSequenceNumber,
            trigger.OccurredAt,
            CreateContextHash(trigger.ForegroundProcessId, mainWindow));
        return true;
    }

    private static string CreateContextHash(uint processId, nint mainWindow)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"Discord|{processId}|{mainWindow.ToInt64():X}"));
        return Convert.ToHexString(bytes);
    }
}
