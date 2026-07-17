param(
    [Parameter(Mandatory = $true)]
    [string]$MainWindowHandle,

    [Parameter(Mandatory = $true)]
    [string]$RendererHandle,

    [ValidateSet('Inspect', 'Clear', 'Paste', 'ShiftEnter', 'Send')]
    [string]$Action,

    [string]$TestText = ''
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

Add-Type -AssemblyName Accessibility
Add-Type -AssemblyName System.Windows.Forms

if (-not ('Sentory.Diagnostics.DiscordInputNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Accessibility;

namespace Sentory.Diagnostics
{
    public sealed class DiscordInputResult
    {
        public int CandidateCount { get; set; }
        public bool FocusAttempted { get; set; }
        public bool FocusSucceeded { get; set; }
        public int ValueLength { get; set; }
        public bool ValueMatchesExpected { get; set; }
        public string State { get; set; }
        public string ErrorType { get; set; }
    }

    public static class DiscordInputNative
    {
        public const uint GetAncestorRoot = 2;
        public const uint KeyEventKeyUp = 2;
        public const byte VirtualKeyControl = 0x11;
        public const byte VirtualKeyV = 0x56;
        public const byte VirtualKeyA = 0x41;
        public const byte VirtualKeyBackspace = 0x08;
        public const byte VirtualKeyShift = 0x10;
        public const byte VirtualKeyEnter = 0x0D;
        public const int ShowWindowRestore = 9;

        private const uint ObjectIdClient = 0xFFFFFFFC;
        private const int RoleSystemText = 42;
        private const int SelectTakeFocus = 1;
        private static readonly Guid AccessibleInterfaceId =
            new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");

        private sealed class AccessibleTarget
        {
            public IAccessible Accessible;
            public object ChildId;
        }

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(
            IntPtr hwnd,
            uint objectId,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out object accessibleObject);

        [DllImport("oleacc.dll")]
        private static extern int AccessibleChildren(
            IAccessible container,
            int childStart,
            int childCount,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            object[] children,
            out int childrenObtained);

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
        public static extern void keybd_event(
            byte virtualKey,
            byte scanCode,
            uint flags,
            UIntPtr extraInfo);

        public static DiscordInputResult InspectInput(
            IntPtr rendererHandle,
            string expectedText,
            bool takeFocus)
        {
            var result = new DiscordInputResult
            {
                ValueLength = -1,
                State = "unavailable",
                ErrorType = string.Empty
            };

            try
            {
                object rawAccessible;
                var interfaceId = AccessibleInterfaceId;
                var resultCode = AccessibleObjectFromWindow(
                    rendererHandle,
                    ObjectIdClient,
                    ref interfaceId,
                    out rawAccessible);
                if (resultCode != 0 || rawAccessible == null)
                {
                    result.ErrorType = "AccessibleObjectFromWindowFailed";
                    return result;
                }

                var targets = new List<AccessibleTarget>();
                var visited = new HashSet<long>();
                var root = (IAccessible)rawAccessible;
                MarkVisited(root, visited);
                FindTextTargets(root, targets, visited, 0);
                result.CandidateCount = targets.Count;

                if (targets.Count != 1)
                {
                    return result;
                }

                var target = targets[0];
                if (takeFocus)
                {
                    result.FocusAttempted = true;
                    try
                    {
                        target.Accessible.accSelect(
                            SelectTakeFocus,
                            target.ChildId);
                        result.FocusSucceeded = true;
                    }
                    catch
                    {
                        result.FocusSucceeded = false;
                    }
                }

                string value = null;
                try
                {
                    value = target.Accessible.get_accValue(target.ChildId);
                }
                catch
                {
                    value = null;
                }

                result.ValueLength = value == null ? 0 : value.Length;
                if (!string.IsNullOrEmpty(expectedText) && value != null)
                {
                    result.ValueMatchesExpected =
                        string.Equals(
                            value.TrimEnd('\r', '\n'),
                            expectedText,
                            StringComparison.Ordinal);
                }

                try
                {
                    var state = target.Accessible.get_accState(target.ChildId);
                    result.State = state is int
                        ? ((int)state).ToString()
                        : "<non-numeric>";
                }
                catch
                {
                    result.State = "unavailable";
                }
            }
            catch (Exception exception)
            {
                result.ErrorType = exception.GetType().Name;
            }

            return result;
        }

        private static void FindTextTargets(
            IAccessible container,
            IList<AccessibleTarget> targets,
            ISet<long> visited,
            int depth)
        {
            if (depth > 60 || targets.Count > 4)
            {
                return;
            }

            var childCount = SafeChildCount(container);
            if (childCount <= 0)
            {
                return;
            }

            var rawChildren = new object[childCount];
            int obtained;
            var childrenResult = AccessibleChildren(
                container,
                0,
                childCount,
                rawChildren,
                out obtained);
            if (childrenResult != 0 && childrenResult != 1)
            {
                return;
            }

            for (var index = 0; index < obtained; index++)
            {
                var rawChild = rawChildren[index];
                IAccessible childAccessible = null;
                object childId = 0;

                if (rawChild is int)
                {
                    childId = rawChild;
                    if (SafeRole(container, childId) == RoleSystemText)
                    {
                        targets.Add(new AccessibleTarget
                        {
                            Accessible = container,
                            ChildId = childId
                        });
                    }

                    try
                    {
                        childAccessible =
                            container.get_accChild(childId) as IAccessible;
                    }
                    catch
                    {
                        childAccessible = null;
                    }
                }
                else
                {
                    childAccessible = rawChild as IAccessible;
                    if (
                        childAccessible != null &&
                        SafeRole(childAccessible, 0) == RoleSystemText)
                    {
                        targets.Add(new AccessibleTarget
                        {
                            Accessible = childAccessible,
                            ChildId = 0
                        });
                    }
                }

                if (
                    childAccessible != null &&
                    MarkVisited(childAccessible, visited))
                {
                    FindTextTargets(
                        childAccessible,
                        targets,
                        visited,
                        depth + 1);
                }
            }
        }

        private static int SafeRole(
            IAccessible accessible,
            object childId)
        {
            try
            {
                var role = accessible.get_accRole(childId);
                return role is int ? (int)role : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeChildCount(IAccessible accessible)
        {
            try
            {
                return accessible.accChildCount;
            }
            catch
            {
                return 0;
            }
        }

        private static bool MarkVisited(
            IAccessible accessible,
            ISet<long> visited)
        {
            IntPtr unknown = IntPtr.Zero;
            try
            {
                unknown = Marshal.GetIUnknownForObject(accessible);
                return visited.Add(unknown.ToInt64());
            }
            catch
            {
                return true;
            }
            finally
            {
                if (unknown != IntPtr.Zero)
                {
                    Marshal.Release(unknown);
                }
            }
        }
    }
}
'@ -ReferencedAssemblies Accessibility
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

function Convert-InputResult {
    param([Sentory.Diagnostics.DiscordInputResult]$Result)

    [pscustomobject]@{
        candidateCount = $Result.CandidateCount
        focusAttempted = $Result.FocusAttempted
        focusSucceeded = $Result.FocusSucceeded
        valueLength = $Result.ValueLength
        valueMatchesExpected = $Result.ValueMatchesExpected
        state = $Result.State
        errorType = $Result.ErrorType
    }
}

$mainHandle = Convert-Handle $MainWindowHandle
$rendererWindowHandle = Convert-Handle $RendererHandle

if (-not [Sentory.Diagnostics.DiscordInputNative]::IsWindow($mainHandle)) {
    throw 'Discord main window handle is no longer valid.'
}

if (-not [Sentory.Diagnostics.DiscordInputNative]::IsWindow(
    $rendererWindowHandle
)) {
    throw 'Discord renderer handle is no longer valid.'
}

$rendererRoot = [Sentory.Diagnostics.DiscordInputNative]::GetAncestor(
    $rendererWindowHandle,
    [Sentory.Diagnostics.DiscordInputNative]::GetAncestorRoot
)
if ($rendererRoot -ne $mainHandle) {
    throw 'The renderer no longer belongs to the prepared Discord window.'
}

$targetProcessId = 0
$targetThreadId =
    [Sentory.Diagnostics.DiscordInputNative]::GetWindowThreadProcessId(
        $mainHandle,
        [ref]$targetProcessId
    )
$foregroundHandle =
    [Sentory.Diagnostics.DiscordInputNative]::GetForegroundWindow()
$foregroundProcessId = 0
$foregroundThreadId =
    [Sentory.Diagnostics.DiscordInputNative]::GetWindowThreadProcessId(
        $foregroundHandle,
        [ref]$foregroundProcessId
    )
$currentThreadId =
    [Sentory.Diagnostics.DiscordInputNative]::GetCurrentThreadId()

$attachedTarget =
    [Sentory.Diagnostics.DiscordInputNative]::AttachThreadInput(
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
        [Sentory.Diagnostics.DiscordInputNative]::AttachThreadInput(
            $currentThreadId,
            $foregroundThreadId,
            $true
        )
}

try {
    [void][Sentory.Diagnostics.DiscordInputNative]::ShowWindow(
        $mainHandle,
        [Sentory.Diagnostics.DiscordInputNative]::ShowWindowRestore
    )
    [void][Sentory.Diagnostics.DiscordInputNative]::BringWindowToTop(
        $mainHandle
    )
    [void][Sentory.Diagnostics.DiscordInputNative]::SetForegroundWindow(
        $mainHandle
    )
    [void][Sentory.Diagnostics.DiscordInputNative]::SetActiveWindow(
        $mainHandle
    )
}
finally {
    if ($attachedForeground) {
        [void][Sentory.Diagnostics.DiscordInputNative]::AttachThreadInput(
            $currentThreadId,
            $foregroundThreadId,
            $false
        )
    }
    if ($attachedTarget) {
        [void][Sentory.Diagnostics.DiscordInputNative]::AttachThreadInput(
            $currentThreadId,
            $targetThreadId,
            $false
        )
    }
}

Start-Sleep -Milliseconds 500
$before = [Sentory.Diagnostics.DiscordInputNative]::InspectInput(
    $rendererWindowHandle,
    $TestText,
    $true
)

if ($before.CandidateCount -ne 1) {
    throw 'Discord message input was not uniquely identified.'
}

if ($Action -eq 'Clear') {
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyControl,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyA,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyA,
        0,
        [Sentory.Diagnostics.DiscordInputNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyControl,
        0,
        [Sentory.Diagnostics.DiscordInputNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyBackspace,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyBackspace,
        0,
        [Sentory.Diagnostics.DiscordInputNative]::KeyEventKeyUp,
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
        [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
            [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyControl,
            0,
            0,
            [UIntPtr]::Zero
        )
        [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
            [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyV,
            0,
            0,
            [UIntPtr]::Zero
        )
        [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
            [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyV,
            0,
            [Sentory.Diagnostics.DiscordInputNative]::KeyEventKeyUp,
            [UIntPtr]::Zero
        )
        [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
            [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyControl,
            0,
            [Sentory.Diagnostics.DiscordInputNative]::KeyEventKeyUp,
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
elseif ($Action -eq 'ShiftEnter') {
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyShift,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyEnter,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyEnter,
        0,
        [Sentory.Diagnostics.DiscordInputNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyShift,
        0,
        [Sentory.Diagnostics.DiscordInputNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    Start-Sleep -Milliseconds 1000
}
elseif ($Action -eq 'Send') {
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyEnter,
        0,
        0,
        [UIntPtr]::Zero
    )
    [Sentory.Diagnostics.DiscordInputNative]::keybd_event(
        [Sentory.Diagnostics.DiscordInputNative]::VirtualKeyEnter,
        0,
        [Sentory.Diagnostics.DiscordInputNative]::KeyEventKeyUp,
        [UIntPtr]::Zero
    )
    Start-Sleep -Milliseconds 2500
}

$after = [Sentory.Diagnostics.DiscordInputNative]::InspectInput(
    $rendererWindowHandle,
    $TestText,
    $false
)

[pscustomobject]@{
    action = $Action
    rootHandleMatches = ($rendererRoot -eq $mainHandle)
    foregroundWindowMatches = (
        [Sentory.Diagnostics.DiscordInputNative]::GetForegroundWindow() -eq
        $mainHandle
    )
    before = Convert-InputResult $before
    after = Convert-InputResult $after
} | ConvertTo-Json -Depth 5 -Compress
