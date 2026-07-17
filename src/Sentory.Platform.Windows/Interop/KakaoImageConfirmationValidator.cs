namespace Sentory.Platform.Windows.Interop;

public sealed class KakaoImageConfirmationValidator(
    INativeWindowApi native)
{
    public const string PreviewWindowClassName = "EVA_Window_Dblclk";
    public const string CaptionEditClassName = "Edit";
    public const int CaptionEditControlId = 100;

    public bool TryValidate(
        ValidatedKakaoContext sourceContext,
        out nint confirmationWindow)
    {
        confirmationWindow = nint.Zero;
        if (native.GetClipboardSequenceNumber() !=
            sourceContext.ClipboardSequenceNumber)
        {
            return false;
        }

        var foreground = native.GetForegroundWindow();
        if (foreground == nint.Zero ||
            foreground == sourceContext.ChatRootWindow ||
            native.GetProcessId(foreground) != sourceContext.ProcessId ||
            native.GetRootWindow(foreground) != foreground ||
            native.GetOwnerWindow(foreground) !=
            sourceContext.ChatRootWindow ||
            !string.Equals(
                native.GetClassName(foreground),
                PreviewWindowClassName,
                StringComparison.Ordinal))
        {
            return false;
        }

        var bounds = native.GetWindowBounds(foreground);
        if (bounds.Width < 350 || bounds.Height < 450)
        {
            return false;
        }

        var focused = native.GetFocusedWindow(foreground);
        if (focused == nint.Zero ||
            native.GetRootWindow(focused) != foreground ||
            !string.Equals(
                native.GetClassName(focused),
                CaptionEditClassName,
                StringComparison.Ordinal) ||
            native.GetControlId(focused) != CaptionEditControlId ||
            !native.HasDescendant(
                foreground,
                CaptionEditClassName,
                CaptionEditControlId))
        {
            return false;
        }

        confirmationWindow = foreground;
        return true;
    }
}
