using System.Security.Cryptography;
using System.Text;

namespace Sentory.Platform.Windows.Interop;

public sealed record PasteTrigger(
    Guid EventId,
    nint ForegroundWindow,
    nint FocusedWindow,
    uint ForegroundProcessId,
    uint ClipboardSequenceNumber,
    DateTimeOffset OccurredAt,
    bool IsInjected);

public sealed record ValidatedKakaoContext(
    Guid EventId,
    nint ChatRootWindow,
    nint InputWindow,
    uint ProcessId,
    uint ClipboardSequenceNumber,
    DateTimeOffset OccurredAt,
    string ContextHash);

public sealed class KakaoContextValidator(INativeWindowApi native)
{
    public const string KakaoProcessName = "KakaoTalk";
    public const string InputClassName = "RICHEDIT50W";
    public const int InputControlId = 1006;
    public const string MessageListClassName =
        "EVA_VH_ListControl_Dblclk";
    public const int MessageListControlId = 100;

    public bool TryValidate(
        PasteTrigger trigger,
        out ValidatedKakaoContext context)
    {
        context = null!;
        if (trigger.ForegroundWindow == nint.Zero ||
            trigger.FocusedWindow == nint.Zero ||
            trigger.ForegroundProcessId == 0)
        {
            return false;
        }

        var processName = native.GetProcessName(trigger.ForegroundProcessId);
        if (!string.Equals(
                processName,
                KakaoProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (native.GetProcessId(trigger.FocusedWindow) !=
            trigger.ForegroundProcessId)
        {
            return false;
        }

        if (!string.Equals(
                native.GetClassName(trigger.FocusedWindow),
                InputClassName,
                StringComparison.Ordinal) ||
            native.GetControlId(trigger.FocusedWindow) != InputControlId)
        {
            return false;
        }

        var foregroundRoot = native.GetRootWindow(trigger.ForegroundWindow);
        var inputRoot = native.GetRootWindow(trigger.FocusedWindow);
        if (foregroundRoot == nint.Zero ||
            inputRoot == nint.Zero ||
            foregroundRoot != inputRoot ||
            native.GetProcessId(inputRoot) != trigger.ForegroundProcessId)
        {
            return false;
        }

        if (!native.HasDescendant(
                inputRoot,
                MessageListClassName,
                MessageListControlId))
        {
            return false;
        }

        context = new ValidatedKakaoContext(
            trigger.EventId,
            inputRoot,
            trigger.FocusedWindow,
            trigger.ForegroundProcessId,
            trigger.ClipboardSequenceNumber,
            trigger.OccurredAt,
            CreateContextHash(trigger.ForegroundProcessId, inputRoot));
        return true;
    }

    private static string CreateContextHash(uint processId, nint root)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"KakaoTalk|{processId}|{root.ToInt64():X}"));
        return Convert.ToHexString(bytes);
    }
}
