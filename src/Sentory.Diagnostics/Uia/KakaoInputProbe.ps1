param(
    [Parameter(Mandatory = $true)]
    [string]$ChatWindowHandle,

    [Parameter(Mandatory = $true)]
    [string]$InputHandle,

    [ValidateSet('Inspect', 'Clear', 'Paste', 'Send')]
    [string]$Action,

    [string]$TestText = ''
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

if (-not ('Sentory.Diagnostics.KakaoInputNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Sentory.Diagnostics
{
    public static class KakaoInputNative
    {
        public const uint GetAncestorRoot = 2;
        public const uint KeyEventKeyUp = 2;
        public const byte VirtualKeyControl = 0x11;
        public const byte VirtualKeyV = 0x56;
        public const byte VirtualKeyA = 0x41;
        public const byte VirtualKeyBackspace = 0x08;
        public const byte VirtualKeyEnter = 0x0D;
        public const int ShowWindowRestore = 9;

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(
            IntPtr windowHandle,
            uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(
            IntPtr windowHandle,
            out uint processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(
            uint attachThreadId,
            uint attachToThreadId,
            bool attach);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(
            IntPtr windowHandle,
            int command);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr SetActiveWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern void keybd_event(
            byte virtualKey,
            byte scanCode,
            uint flags,
            UIntPtr extraInfo);

        [DllImport(
            "user32.dll",
            EntryPoint = "GetClassNameW",
            CharSet = CharSet.Unicode)]
        public static extern int GetClassName(
            IntPtr windowHandle,
            StringBuilder className,
            int maxCount);
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

$chatHandle = Convert-Handle $ChatWindowHandle
$inputWindowHandle = Convert-Handle $InputHandle

if (-not [Sentory.Diagnostics.KakaoInputNative]::IsWindow($chatHandle)) {
    throw 'Kakao chat window handle is no longer valid.'
}

if (-not [Sentory.Diagnostics.KakaoInputNative]::IsWindow($inputWindowHandle)) {
    throw 'Kakao input handle is no longer valid.'
}

$rootHandle = [Sentory.Diagnostics.KakaoInputNative]::GetAncestor(
    $inputWindowHandle,
    [Sentory.Diagnostics.KakaoInputNative]::GetAncestorRoot
)
if ($rootHandle -ne $chatHandle) {
    throw 'The input handle no longer belongs to the prepared Kakao chat window.'
}

$className = New-Object Text.StringBuilder 128
[void][Sentory.Diagnostics.KakaoInputNative]::GetClassName(
    $inputWindowHandle,
    $className,
    $className.Capacity
)
if ($className.ToString() -ne 'RICHEDIT50W') {
    throw 'The prepared Kakao input handle is not a RICHEDIT50W control.'
}

$element = [System.Windows.Automation.AutomationElement]::FromHandle(
    $inputWindowHandle
)
$beforeLength = [Sentory.Diagnostics.KakaoInputNative]::GetWindowTextLength(
    $inputWindowHandle
)

$targetProcessId = 0
$targetThreadId = [Sentory.Diagnostics.KakaoInputNative]::GetWindowThreadProcessId(
    $chatHandle,
    [ref]$targetProcessId
)
$foregroundHandle = [Sentory.Diagnostics.KakaoInputNative]::GetForegroundWindow()
$foregroundProcessId = 0
$foregroundThreadId =
    [Sentory.Diagnostics.KakaoInputNative]::GetWindowThreadProcessId(
        $foregroundHandle,
        [ref]$foregroundProcessId
    )
$currentThreadId =
    [Sentory.Diagnostics.KakaoInputNative]::GetCurrentThreadId()

$attachedTarget =
    [Sentory.Diagnostics.KakaoInputNative]::AttachThreadInput(
        $currentThreadId,
        $targetThreadId,
        $true
    )
$attachedForeground = $false
if (
    $foregroundThreadId -ne 0 -and
    $foregroundThreadId -ne $targetThreadId
) {
    $attachedForeground =
        [Sentory.Diagnostics.KakaoInputNative]::AttachThreadInput(
            $currentThreadId,
            $foregroundThreadId,
            $true
        )
}

try {
    [void][Sentory.Diagnostics.KakaoInputNative]::ShowWindow(
        $chatHandle,
        [Sentory.Diagnostics.KakaoInputNative]::ShowWindowRestore
    )
    [void][Sentory.Diagnostics.KakaoInputNative]::BringWindowToTop($chatHandle)
    [void][Sentory.Diagnostics.KakaoInputNative]::SetForegroundWindow($chatHandle)
    [void][Sentory.Diagnostics.KakaoInputNative]::SetActiveWindow($chatHandle)
    [void][Sentory.Diagnostics.KakaoInputNative]::SetFocus($inputWindowHandle)
}
finally {
    if ($attachedForeground) {
        [void][Sentory.Diagnostics.KakaoInputNative]::AttachThreadInput(
            $currentThreadId,
            $foregroundThreadId,
            $false
        )
    }
    if ($attachedTarget) {
        [void][Sentory.Diagnostics.KakaoInputNative]::AttachThreadInput(
            $currentThreadId,
            $targetThreadId,
            $false
        )
    }
}

Start-Sleep -Milliseconds 500

if ($Action -eq 'Clear') {
    [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
        [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyControl,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
        [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyA,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
        [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyA,
        0,
        [Sentory.Diagnostics.KakaoInputNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
        [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyControl,
        0,
        [Sentory.Diagnostics.KakaoInputNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
        [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyBackspace,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
        [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyBackspace,
        0,
        [Sentory.Diagnostics.KakaoInputNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    Start-Sleep -Milliseconds 800
}
elseif ($Action -eq 'Paste') {
    if ([string]::IsNullOrEmpty($TestText)) {
        throw 'TestText is required for Paste.'
    }

    $previousClipboard = [System.Windows.Forms.Clipboard]::GetDataObject()
    try {
        [System.Windows.Forms.Clipboard]::SetText($TestText)
        [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
            [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyControl,
            0,
            0,
            [UIntPtr]::Zero
        )
        [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
            [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyV,
            0,
            0,
            [UIntPtr]::Zero
        )
        [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
            [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyV,
            0,
            [Sentory.Diagnostics.KakaoInputNative]::KeyEventKeyUp,
            [UIntPtr]::Zero
        )
        [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
            [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyControl,
            0,
            [Sentory.Diagnostics.KakaoInputNative]::KeyEventKeyUp,
            [UIntPtr]::Zero
        )
        Start-Sleep -Milliseconds 1200
    }
    finally {
        if ($null -ne $previousClipboard) {
            [System.Windows.Forms.Clipboard]::SetDataObject(
                $previousClipboard,
                $true
            )
        }
    }
}
elseif ($Action -eq 'Send') {
    [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
        [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyEnter,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.KakaoInputNative]::keybd_event(
        [Sentory.Diagnostics.KakaoInputNative]::VirtualKeyEnter,
        0,
        [Sentory.Diagnostics.KakaoInputNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    Start-Sleep -Milliseconds 2500
}

$afterLength = [Sentory.Diagnostics.KakaoInputNative]::GetWindowTextLength(
    $inputWindowHandle
)

$valuePatternLength = -1
$valuePatternMatchesTest = $false
try {
    $valuePattern = [System.Windows.Automation.ValuePattern]$element.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern
    )
    $valuePatternText = $valuePattern.Current.Value
    $valuePatternLength = if ($null -eq $valuePatternText) {
        0
    }
    else {
        $valuePatternText.Length
    }
    if (-not [string]::IsNullOrEmpty($TestText)) {
        $valuePatternMatchesTest = (
            $valuePatternText.TrimEnd([char[]]"`r`n") -eq $TestText
        )
    }
}
catch {
    $valuePatternLength = -1
}

$textPatternLength = -1
$textPatternMatchesTest = $false
try {
    $textPattern = [System.Windows.Automation.TextPattern]$element.GetCurrentPattern(
        [System.Windows.Automation.TextPattern]::Pattern
    )
    $textPatternText = $textPattern.DocumentRange.GetText(-1)
    $textPatternLength = if ($null -eq $textPatternText) {
        0
    }
    else {
        $textPatternText.Length
    }
    if (-not [string]::IsNullOrEmpty($TestText)) {
        $textPatternMatchesTest = (
            $textPatternText.TrimEnd([char[]]"`r`n") -eq $TestText
        )
    }
}
catch {
    $textPatternLength = -1
}

[pscustomobject]@{
    action = $Action
    beforeLength = $beforeLength
    afterLength = $afterLength
    hasKeyboardFocus = $element.Current.HasKeyboardFocus
    inputClass = $className.ToString()
    rootHandleMatches = ($rootHandle -eq $chatHandle)
    foregroundWindowMatches = (
        [Sentory.Diagnostics.KakaoInputNative]::GetForegroundWindow() -eq
        $chatHandle
    )
    valuePatternLength = $valuePatternLength
    valuePatternMatchesTest = $valuePatternMatchesTest
    textPatternLength = $textPatternLength
    textPatternMatchesTest = $textPatternMatchesTest
} | ConvertTo-Json -Compress
