param(
    [Parameter(Mandatory = $true)]
    [string]$ChatWindowHandle,

    [Parameter(Mandatory = $true)]
    [string]$DialogWindowHandle
)

$ErrorActionPreference = 'Stop'

if (-not ('Sentory.Diagnostics.KakaoImageDialogCancelNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Sentory.Diagnostics
{
    public static class KakaoImageDialogCancelNative
    {
        public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(
            IntPtr window,
            StringBuilder className,
            int maximumCount);

        [DllImport("user32.dll")]
        public static extern int GetDlgCtrlID(IntPtr window);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(
            IntPtr parent,
            EnumWindowsProc callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(
            IntPtr window,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);
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
    [void][Sentory.Diagnostics.KakaoImageDialogCancelNative]::GetClassName(
        $Window,
        $buffer,
        $buffer.Capacity
    )
    return $buffer.ToString()
}

$chat = Convert-Handle $ChatWindowHandle
$dialog = Convert-Handle $DialogWindowHandle
if (
    -not [Sentory.Diagnostics.KakaoImageDialogCancelNative]::IsWindow($chat) -or
    -not [Sentory.Diagnostics.KakaoImageDialogCancelNative]::IsWindow($dialog) -or
    [Sentory.Diagnostics.KakaoImageDialogCancelNative]::GetWindow($dialog, 4) -ne
        $chat -or
    (Get-ClassName $dialog) -ne 'EVA_Window_Dblclk'
) {
    throw 'The target is not an owned Kakao image dialog.'
}

$hasCaptionEdit = $false
$callback = {
    param([IntPtr]$window, [IntPtr]$parameter)
    if (
        (Get-ClassName $window) -eq 'Edit' -and
        [Sentory.Diagnostics.KakaoImageDialogCancelNative]::GetDlgCtrlID(
            $window
        ) -eq 100
    ) {
        $script:hasCaptionEdit = $true
        return $false
    }
    return $true
}
[void][Sentory.Diagnostics.KakaoImageDialogCancelNative]::EnumChildWindows(
    $dialog,
    $callback,
    [IntPtr]::Zero
)
if (-not $hasCaptionEdit) {
    throw 'The Kakao dialog does not match the image preview signature.'
}

if (
    -not [Sentory.Diagnostics.KakaoImageDialogCancelNative]::PostMessage(
        $dialog,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero
    )
) {
    throw 'The Kakao image dialog could not be closed.'
}

Start-Sleep -Milliseconds 500
[pscustomobject]@{
    dialogClosed =
        -not [Sentory.Diagnostics.KakaoImageDialogCancelNative]::IsWindow(
            $dialog
        )
} | ConvertTo-Json -Compress
