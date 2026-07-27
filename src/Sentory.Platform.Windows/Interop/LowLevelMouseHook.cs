using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sentory.Platform.Windows.Interop;

public sealed record PointerTrigger(
    Guid EventId,
    nint ForegroundWindow,
    uint ForegroundProcessId,
    int ScreenX,
    int ScreenY,
    DateTimeOffset OccurredAt,
    bool Injected);

public sealed class LowLevelMouseHook : IDisposable
{
    private readonly INativeWindowApi _native;
    private readonly bool _acceptInjectedInput;
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private nint _hook;
    private long _lastTriggerTick;

    public LowLevelMouseHook(
        INativeWindowApi native,
        bool acceptInjectedInput = false)
    {
        _native = native;
        _acceptInjectedInput = acceptInjectedInput;
        _callback = HookCallback;
    }

    public event EventHandler<PointerTrigger>? LeftButtonDown;

    public void Start()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook == nint.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "마우스 전송 감지 훅을 설치하지 못했습니다.");
        }
    }

    private nint HookCallback(
        int code,
        nint message,
        nint mouseData)
    {
        if (code >= 0 && message == NativeMethods.WmLButtonDown)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(
                mouseData);
            var injected =
                (data.Flags & NativeMethods.LlmhfInjected) != 0;
            if (_acceptInjectedInput || !injected)
            {
                var now = Environment.TickCount64;
                if (now - Interlocked.Read(ref _lastTriggerTick) >= 120)
                {
                    Interlocked.Exchange(ref _lastTriggerTick, now);
                    RaiseLeftButtonDown(data, injected);
                }
            }
        }

        return NativeMethods.CallNextHookEx(
            _hook,
            code,
            message,
            mouseData);
    }

    private void RaiseLeftButtonDown(
        NativeMethods.MouseHookData data,
        bool injected)
    {
        var foreground = _native.GetForegroundWindow();
        LeftButtonDown?.Invoke(
            this,
            new PointerTrigger(
                Guid.NewGuid(),
                foreground,
                _native.GetProcessId(foreground),
                data.Point.X,
                data.Point.Y,
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
