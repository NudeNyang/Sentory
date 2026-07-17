param(
    [string]$TestText = '',

    [switch]$Image
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not $Image -and [string]::IsNullOrWhiteSpace($TestText)) {
    throw 'TestText is required unless Image is selected.'
}

if (-not ('Sentory.Diagnostics.NotepadPasteNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace Sentory.Diagnostics
{
    public static class NotepadPasteNative
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr SetActiveWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr window, int command);

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
    }
}
'@
}

$existingIds = @(
    Get-Process notepad -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Id }
)
$started = Start-Process notepad.exe -PassThru
$target = $null

try {
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        Start-Sleep -Milliseconds 100
        $candidates = @(
            Get-Process notepad -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.Id -notin $existingIds -and
                    $_.MainWindowHandle -ne 0
                }
        )
        $target = $candidates | Select-Object -First 1
        if ($null -ne $target) {
            break
        }
    }

    if ($null -eq $target) {
        throw 'The disposable Notepad window was not created.'
    }

    $foreground = [Sentory.Diagnostics.NotepadPasteNative]::GetForegroundWindow()
    $foregroundProcessId = 0
    $foregroundThread =
        [Sentory.Diagnostics.NotepadPasteNative]::GetWindowThreadProcessId(
            $foreground,
            [ref]$foregroundProcessId
        )
    $targetProcessId = 0
    $targetThread =
        [Sentory.Diagnostics.NotepadPasteNative]::GetWindowThreadProcessId(
            $target.MainWindowHandle,
            [ref]$targetProcessId
        )
    $currentThread =
        [Sentory.Diagnostics.NotepadPasteNative]::GetCurrentThreadId()
    $attachedTarget =
        [Sentory.Diagnostics.NotepadPasteNative]::AttachThreadInput(
            $currentThread,
            $targetThread,
            $true
        )
    $attachedForeground = $false
    if ($foregroundThread -ne 0 -and $foregroundThread -ne $targetThread) {
        $attachedForeground =
            [Sentory.Diagnostics.NotepadPasteNative]::AttachThreadInput(
                $currentThread,
                $foregroundThread,
                $true
            )
    }
    try {
        [void][Sentory.Diagnostics.NotepadPasteNative]::ShowWindow(
            $target.MainWindowHandle,
            9
        )
        [void][Sentory.Diagnostics.NotepadPasteNative]::BringWindowToTop(
            $target.MainWindowHandle
        )
        [void][Sentory.Diagnostics.NotepadPasteNative]::SetForegroundWindow(
            $target.MainWindowHandle
        )
        [void][Sentory.Diagnostics.NotepadPasteNative]::SetActiveWindow(
            $target.MainWindowHandle
        )
    }
    finally {
        if ($attachedForeground) {
            [void][Sentory.Diagnostics.NotepadPasteNative]::AttachThreadInput(
                $currentThread,
                $foregroundThread,
                $false
            )
        }
        if ($attachedTarget) {
            [void][Sentory.Diagnostics.NotepadPasteNative]::AttachThreadInput(
                $currentThread,
                $targetThread,
                $false
            )
        }
    }
    Start-Sleep -Milliseconds 500
    if (
        [Sentory.Diagnostics.NotepadPasteNative]::GetForegroundWindow() -ne
            $target.MainWindowHandle
    ) {
        throw 'The disposable Notepad window did not become foreground.'
    }

    $previousClipboard = [System.Windows.Forms.Clipboard]::GetDataObject()
    $bitmap = $null
    $graphics = $null
    try {
        if ($Image) {
            $bitmap = New-Object Drawing.Bitmap 48, 32
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $graphics.Clear([Drawing.Color]::FromArgb(255, 37, 99, 235))
            [System.Windows.Forms.Clipboard]::SetImage($bitmap)
        }
        else {
            [System.Windows.Forms.Clipboard]::SetText($TestText)
        }
        [System.Windows.Forms.SendKeys]::SendWait('^v')
        Start-Sleep -Milliseconds 1200
    }
    finally {
        if ($null -ne $graphics) {
            $graphics.Dispose()
        }
        if ($null -ne $bitmap) {
            $bitmap.Dispose()
        }
        if ($null -ne $previousClipboard) {
            [System.Windows.Forms.Clipboard]::SetDataObject(
                $previousClipboard,
                $true
            )
        }
    }

    [pscustomobject]@{
        processId = $target.Id
        foregroundRequested = $true
        pasteRequested = $true
    } | ConvertTo-Json -Compress
}
finally {
    if ($null -ne $target -and -not $target.HasExited) {
        $target.Kill()
        [void]$target.WaitForExit(3000)
    }
    elseif (-not $started.HasExited) {
        $started.Kill()
        [void]$started.WaitForExit(3000)
    }
}
