param(
    [Parameter(Mandatory = $true)]
    [string]$ChatWindowHandle,

    [Parameter(Mandatory = $true)]
    [string]$InputHandle
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient

if (-not ('Sentory.Diagnostics.KakaoImagePasteNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Sentory.Diagnostics
{
    public static class KakaoImagePasteNative
    {
        public const uint GetAncestorRoot = 2;
        public const uint KeyEventKeyUp = 2;
        public const byte VirtualKeyControl = 0x11;
        public const byte VirtualKeyV = 0x56;
        public const byte VirtualKeyEscape = 0x1B;

        public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        public struct GuiThreadInfo
        {
            public uint Size;
            public uint Flags;
            public IntPtr Active;
            public IntPtr Focus;
            public IntPtr Capture;
            public IntPtr MenuOwner;
            public IntPtr MoveSize;
            public IntPtr Caret;
            public Rect CaretRect;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr window);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr SetActiveWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out uint processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(
            uint attachThreadId,
            uint attachToThreadId,
            bool attach);

        [DllImport("user32.dll")]
        public static extern bool GetGUIThreadInfo(
            uint threadId,
            ref GuiThreadInfo info);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(
            EnumWindowsProc callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(
            IntPtr parent,
            EnumWindowsProc callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(
            IntPtr window,
            StringBuilder className,
            int maximumCount);

        [DllImport("user32.dll")]
        public static extern int GetDlgCtrlID(IntPtr window);

        [DllImport("user32.dll")]
        public static extern void keybd_event(
            byte virtualKey,
            byte scanCode,
            uint flags,
            UIntPtr extraInfo);
    }
}
'@
}

function Convert-Handle {
    param([string]$Value)
    $numericText = if ($Value.StartsWith('0x')) {
        $Value.Substring(2)
    }
    else {
        $Value
    }
    return [IntPtr][Convert]::ToInt64($numericText, 16)
}

function Get-ClassName {
    param([IntPtr]$Window)
    $buffer = New-Object Text.StringBuilder 256
    [void][Sentory.Diagnostics.KakaoImagePasteNative]::GetClassName(
        $Window,
        $buffer,
        $buffer.Capacity
    )
    return $buffer.ToString()
}

function Get-KakaoTopLevelWindows {
    param([uint32]$ProcessId)
    $items = New-Object Collections.Generic.List[object]
    $callback = {
        param([IntPtr]$window, [IntPtr]$parameter)
        $windowProcessId = 0
        [void][Sentory.Diagnostics.KakaoImagePasteNative]::GetWindowThreadProcessId(
            $window,
            [ref]$windowProcessId
        )
        if (
            $windowProcessId -eq $ProcessId -and
            [Sentory.Diagnostics.KakaoImagePasteNative]::IsWindowVisible($window)
        ) {
            $items.Add([pscustomobject]@{
                handle = ('0x{0:X}' -f $window.ToInt64())
                className = Get-ClassName $window
            })
        }
        return $true
    }
    [void][Sentory.Diagnostics.KakaoImagePasteNative]::EnumWindows(
        $callback,
        [IntPtr]::Zero
    )
    return $items.ToArray()
}

function Get-DescendantSignature {
    param([IntPtr]$Root)
    $items = New-Object Collections.Generic.List[object]
    $callback = {
        param([IntPtr]$window, [IntPtr]$parameter)
        $items.Add([pscustomobject]@{
            className = Get-ClassName $window
            controlId =
                [Sentory.Diagnostics.KakaoImagePasteNative]::GetDlgCtrlID($window)
        })
        return $true
    }
    [void][Sentory.Diagnostics.KakaoImagePasteNative]::EnumChildWindows(
        $Root,
        $callback,
        [IntPtr]::Zero
    )
    return $items.ToArray()
}

$chat = Convert-Handle $ChatWindowHandle
$input = Convert-Handle $InputHandle
if (
    -not [Sentory.Diagnostics.KakaoImagePasteNative]::IsWindow($chat) -or
    -not [Sentory.Diagnostics.KakaoImagePasteNative]::IsWindow($input) -or
    [Sentory.Diagnostics.KakaoImagePasteNative]::GetAncestor(
        $input,
        [Sentory.Diagnostics.KakaoImagePasteNative]::GetAncestorRoot
    ) -ne $chat
) {
    throw 'The prepared Kakao chat input is no longer valid.'
}

$processId = 0
[void][Sentory.Diagnostics.KakaoImagePasteNative]::GetWindowThreadProcessId(
    $chat,
    [ref]$processId
)
$beforeWindows = Get-KakaoTopLevelWindows $processId
$element = [System.Windows.Automation.AutomationElement]::FromHandle($input)
$beforeValue = ''
try {
    $pattern = [System.Windows.Automation.ValuePattern]$element.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern
    )
    $beforeValue = $pattern.Current.Value
}
catch {
}

$foregroundBefore = [Sentory.Diagnostics.KakaoImagePasteNative]::GetForegroundWindow()
$foregroundProcessId = 0
$foregroundThread = [Sentory.Diagnostics.KakaoImagePasteNative]::GetWindowThreadProcessId(
    $foregroundBefore,
    [ref]$foregroundProcessId
)
$targetProcessId = 0
$targetThread = [Sentory.Diagnostics.KakaoImagePasteNative]::GetWindowThreadProcessId(
    $chat,
    [ref]$targetProcessId
)
$currentThread = [Sentory.Diagnostics.KakaoImagePasteNative]::GetCurrentThreadId()
$attachedTarget =
    [Sentory.Diagnostics.KakaoImagePasteNative]::AttachThreadInput(
        $currentThread,
        $targetThread,
        $true
    )
$attachedForeground = $false
if ($foregroundThread -ne 0 -and $foregroundThread -ne $targetThread) {
    $attachedForeground =
        [Sentory.Diagnostics.KakaoImagePasteNative]::AttachThreadInput(
            $currentThread,
            $foregroundThread,
            $true
        )
}
try {
    [void][Sentory.Diagnostics.KakaoImagePasteNative]::ShowWindow($chat, 9)
    [void][Sentory.Diagnostics.KakaoImagePasteNative]::BringWindowToTop($chat)
    [void][Sentory.Diagnostics.KakaoImagePasteNative]::SetForegroundWindow($chat)
    [void][Sentory.Diagnostics.KakaoImagePasteNative]::SetActiveWindow($chat)
    [void][Sentory.Diagnostics.KakaoImagePasteNative]::SetFocus($input)
}
finally {
    if ($attachedForeground) {
        [void][Sentory.Diagnostics.KakaoImagePasteNative]::AttachThreadInput(
            $currentThread,
            $foregroundThread,
            $false
        )
    }
    if ($attachedTarget) {
        [void][Sentory.Diagnostics.KakaoImagePasteNative]::AttachThreadInput(
            $currentThread,
            $targetThread,
            $false
        )
    }
}
Start-Sleep -Milliseconds 400
if (
    [Sentory.Diagnostics.KakaoImagePasteNative]::GetForegroundWindow() -ne
        $chat
) {
    throw 'The Kakao chat window did not become foreground.'
}

$bitmap = New-Object Drawing.Bitmap 48, 32
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$previousClipboard = [System.Windows.Forms.Clipboard]::GetDataObject()
try {
    $graphics.Clear([Drawing.Color]::FromArgb(255, 37, 99, 235))
    [System.Windows.Forms.Clipboard]::SetImage($bitmap)
    [Sentory.Diagnostics.KakaoImagePasteNative]::keybd_event(
        [Sentory.Diagnostics.KakaoImagePasteNative]::VirtualKeyControl,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoImagePasteNative]::keybd_event(
        [Sentory.Diagnostics.KakaoImagePasteNative]::VirtualKeyV,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoImagePasteNative]::keybd_event(
        [Sentory.Diagnostics.KakaoImagePasteNative]::VirtualKeyV,
        0,
        [Sentory.Diagnostics.KakaoImagePasteNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoImagePasteNative]::keybd_event(
        [Sentory.Diagnostics.KakaoImagePasteNative]::VirtualKeyControl,
        0,
        [Sentory.Diagnostics.KakaoImagePasteNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    Start-Sleep -Milliseconds 1200

    $afterWindows = Get-KakaoTopLevelWindows $processId
    $beforeHandles = @($beforeWindows | ForEach-Object { $_.handle })
    $newWindow = $afterWindows |
        Where-Object { $_.handle -notin $beforeHandles } |
        Select-Object -First 1
    $newWindowSignature = @()
    $newWindowOwner = ''
    $newWindowWidth = 0
    $newWindowHeight = 0
    if ($null -ne $newWindow) {
        $newWindowHandle = Convert-Handle $newWindow.handle
        $newWindowSignature = Get-DescendantSignature $newWindowHandle
        $owner = [Sentory.Diagnostics.KakaoImagePasteNative]::GetWindow(
            $newWindowHandle,
            4
        )
        $newWindowOwner = '0x{0:X}' -f $owner.ToInt64()
        $rect = New-Object Sentory.Diagnostics.KakaoImagePasteNative+Rect
        if (
            [Sentory.Diagnostics.KakaoImagePasteNative]::GetWindowRect(
                $newWindowHandle,
                [ref]$rect
            )
        ) {
            $newWindowWidth = $rect.Right - $rect.Left
            $newWindowHeight = $rect.Bottom - $rect.Top
        }
    }
    $foreground = [Sentory.Diagnostics.KakaoImagePasteNative]::GetForegroundWindow()
    $foregroundThread = [Sentory.Diagnostics.KakaoImagePasteNative]::GetWindowThreadProcessId(
        $foreground,
        [ref]$processId
    )
    $gui = New-Object Sentory.Diagnostics.KakaoImagePasteNative+GuiThreadInfo
    $gui.Size = [Runtime.InteropServices.Marshal]::SizeOf($gui)
    [void][Sentory.Diagnostics.KakaoImagePasteNative]::GetGUIThreadInfo(
        $foregroundThread,
        [ref]$gui
    )

    $afterValue = ''
    try {
        $pattern = [System.Windows.Automation.ValuePattern]$element.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern
        )
        $afterValue = $pattern.Current.Value
    }
    catch {
    }

    $result = [pscustomobject]@{
        beforeInputLength = $beforeValue.Length
        afterInputLength = $afterValue.Length
        beforeTopLevel = $beforeWindows
        afterTopLevel = $afterWindows
        newTopLevel = $newWindow
        newTopLevelDescendants = $newWindowSignature
        newTopLevelOwner = $newWindowOwner
        newTopLevelWidth = $newWindowWidth
        newTopLevelHeight = $newWindowHeight
        foregroundClass = Get-ClassName $foreground
        focusedClass = Get-ClassName $gui.Focus
        focusedControlId =
            [Sentory.Diagnostics.KakaoImagePasteNative]::GetDlgCtrlID($gui.Focus)
    }

    [Sentory.Diagnostics.KakaoImagePasteNative]::keybd_event(
        [Sentory.Diagnostics.KakaoImagePasteNative]::VirtualKeyEscape,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoImagePasteNative]::keybd_event(
        [Sentory.Diagnostics.KakaoImagePasteNative]::VirtualKeyEscape,
        0,
        [Sentory.Diagnostics.KakaoImagePasteNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    Start-Sleep -Milliseconds 500
    $result | ConvertTo-Json -Depth 5 -Compress
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
    if ($null -ne $previousClipboard) {
        [System.Windows.Forms.Clipboard]::SetDataObject(
            $previousClipboard,
            $true
        )
    }
}
