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

    public bool TryValidateTarget(
        KakaoDropTarget target,
        uint clipboardSequenceNumber,
        DateTimeOffset occurredAt,
        out ValidatedKakaoContext context)
    {
        context = null!;
        if (target.ChatRootWindow == nint.Zero ||
            target.InputWindow == nint.Zero ||
            target.ProcessId == 0 ||
            native.GetRootWindow(target.ChatRootWindow) !=
            target.ChatRootWindow ||
            native.GetRootWindow(target.InputWindow) !=
            target.ChatRootWindow ||
            native.GetProcessId(target.ChatRootWindow) != target.ProcessId ||
            native.GetProcessId(target.InputWindow) != target.ProcessId ||
            !string.Equals(
                native.GetProcessName(target.ProcessId),
                KakaoProcessName,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                native.GetClassName(target.InputWindow),
                InputClassName,
                StringComparison.Ordinal) ||
            native.GetControlId(target.InputWindow) != InputControlId ||
            !native.HasDescendant(
                target.ChatRootWindow,
                MessageListClassName,
                MessageListControlId))
        {
            return false;
        }

        context = new ValidatedKakaoContext(
            Guid.NewGuid(),
            target.ChatRootWindow,
            target.InputWindow,
            target.ProcessId,
            clipboardSequenceNumber,
            occurredAt,
            CreateContextHash(
                target.ProcessId,
                target.ChatRootWindow));
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
