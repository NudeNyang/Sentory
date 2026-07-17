using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Sentory.Platform.Windows.Interop;

public readonly record struct WindowBounds(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public interface INativeWindowApi
{
    nint GetForegroundWindow();
    nint GetFocusedWindow(nint foregroundWindow);
    nint GetRootWindow(nint window);
    uint GetProcessId(nint window);
    string? GetProcessName(uint processId);
    string GetClassName(nint window);
    int GetControlId(nint window);
    nint GetOwnerWindow(nint window);
    WindowBounds GetWindowBounds(nint window);
    bool HasDescendant(nint root, string className, int controlId);
    uint GetClipboardSequenceNumber();
}

public sealed class NativeWindowApi : INativeWindowApi
{
    public nint GetForegroundWindow() =>
        NativeMethods.GetForegroundWindow();

    public nint GetFocusedWindow(nint foregroundWindow)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(
            foregroundWindow,
            out _);
        if (threadId == 0)
        {
            return nint.Zero;
        }

        var info = new NativeMethods.GuiThreadInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>()
        };
        return NativeMethods.GetGUIThreadInfo(threadId, ref info)
            ? info.Focus
            : nint.Zero;
    }

    public nint GetRootWindow(nint window) =>
        NativeMethods.GetAncestor(window, NativeMethods.GetAncestorRoot);

    public uint GetProcessId(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    public string? GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public string GetClassName(nint window)
    {
        var buffer = new StringBuilder(256);
        return NativeMethods.GetClassName(window, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    public int GetControlId(nint window) =>
        NativeMethods.GetDlgCtrlID(window);

    public nint GetOwnerWindow(nint window) =>
        NativeMethods.GetWindow(window, NativeMethods.GetWindowOwner);

    public WindowBounds GetWindowBounds(nint window)
    {
        return NativeMethods.GetWindowRect(window, out var bounds)
            ? new WindowBounds(
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom)
            : default;
    }

    public bool HasDescendant(
        nint root,
        string className,
        int controlId)
    {
        var found = false;
        NativeMethods.EnumChildWindows(
            root,
            (window, _) =>
            {
                if (string.Equals(
                        GetClassName(window),
                        className,
                        StringComparison.Ordinal) &&
                    GetControlId(window) == controlId)
                {
                    found = true;
                    return false;
                }

                return true;
            },
            nint.Zero);
        return found;
    }

    public uint GetClipboardSequenceNumber() =>
        NativeMethods.GetClipboardSequenceNumber();
}

internal static class NativeMethods
{
    internal const uint GetAncestorRoot = 2;
    internal const uint GetWindowOwner = 4;
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmSysKeyDown = 0x0104;
    internal const int VkControl = 0x11;
    internal const int VkV = 0x56;
    internal const uint LlkhfInjected = 0x10;

    internal delegate bool EnumWindowsProc(nint window, nint parameter);
    internal delegate nint LowLevelKeyboardProc(
        int code,
        nint message,
        nint keyboardData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public Rect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHookData
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(
        uint threadId,
        ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(
        nint window,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    internal static extern int GetDlgCtrlID(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(
        nint window,
        out Rect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(
        nint parent,
        EnumWindowsProc callback,
        nint parameter);

    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(
        nint hook,
        int code,
        nint message,
        nint keyboardData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);
}
