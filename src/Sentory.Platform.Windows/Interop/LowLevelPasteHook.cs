using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sentory.Platform.Windows.Interop;

public sealed class LowLevelPasteHook : IDisposable
{
    private readonly INativeWindowApi _native;
    private readonly bool _acceptInjectedInput;
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private nint _hook;
    private long _lastTriggerTick;

    public LowLevelPasteHook(
        INativeWindowApi native,
        bool acceptInjectedInput = false)
    {
        _native = native;
        _acceptInjectedInput = acceptInjectedInput;
        _callback = HookCallback;
    }

    public event EventHandler<PasteTrigger>? PasteDetected;

    public void Start()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook == nint.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Ctrl+V 감지 훅을 설치하지 못했습니다.");
        }
    }

    private nint HookCallback(
        int code,
        nint message,
        nint keyboardData)
    {
        if (code >= 0 &&
            (message == NativeMethods.WmKeyDown ||
             message == NativeMethods.WmSysKeyDown))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(
                keyboardData);
            var injected =
                (data.Flags & NativeMethods.LlkhfInjected) != 0;
            var controlDown =
                (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) &
                 0x8000) != 0;
            if (data.VirtualKeyCode == NativeMethods.VkV &&
                controlDown &&
                (_acceptInjectedInput || !injected))
            {
                var now = Environment.TickCount64;
                if (now - Interlocked.Read(ref _lastTriggerTick) >= 120)
                {
                    Interlocked.Exchange(ref _lastTriggerTick, now);
                    RaisePasteDetected(injected);
                }
            }
        }

        return NativeMethods.CallNextHookEx(
            _hook,
            code,
            message,
            keyboardData);
    }

    private void RaisePasteDetected(bool injected)
    {
        var foreground = _native.GetForegroundWindow();
        var processId = _native.GetProcessId(foreground);
        var focused = _native.GetFocusedWindow(foreground);
        PasteDetected?.Invoke(
            this,
            new PasteTrigger(
                Guid.NewGuid(),
                foreground,
                focused,
                processId,
                _native.GetClipboardSequenceNumber(),
                DateTimeOffset.UtcNow,
                injected));
    }

    public void Dispose()
    {
        var hook = Interlocked.Exchange(ref _hook, nint.Zero);
        if (hook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(hook);
        }

        GC.SuppressFinalize(this);
    }
}
