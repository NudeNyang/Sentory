param(
    [Parameter(Mandatory = $true)]
    [string]$Handles,

    [ValidateRange(1, 10000)]
    [int]$MaxChildren = 1000
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

Add-Type -AssemblyName Accessibility

if (-not ('Sentory.Diagnostics.MsaaNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Accessibility;

namespace Sentory.Diagnostics
{
    public sealed class MsaaNode
    {
        public string Kind { get; set; }
        public string Role { get; set; }
        public string State { get; set; }
        public int NameLength { get; set; }
        public int? ChildCount { get; set; }
    }

    public sealed class MsaaResult
    {
        public int ResultCode { get; set; }
        public bool Available { get; set; }
        public MsaaNode Root { get; set; }
        public MsaaNode[] Children { get; set; }
        public string ErrorType { get; set; }
    }

    public static class MsaaNative
    {
        private const uint ObjectIdClient = 0xFFFFFFFC;
        private static readonly Guid AccessibleInterfaceId =
            new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");

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

        public static MsaaResult Probe(IntPtr windowHandle, int maxChildren)
        {
            var result = new MsaaResult
            {
                Children = new MsaaNode[0],
                ErrorType = string.Empty
            };

            try
            {
                object rawAccessible;
                var interfaceId = AccessibleInterfaceId;
                result.ResultCode = AccessibleObjectFromWindow(
                    windowHandle,
                    ObjectIdClient,
                    ref interfaceId,
                    out rawAccessible);

                if (result.ResultCode != 0 || rawAccessible == null)
                {
                    return result;
                }

                var accessible = (IAccessible)rawAccessible;
                result.Available = true;

                var childCount = SafeChildCount(accessible);
                result.Root = CreateNode(
                    accessible,
                    0,
                    "root",
                    childCount);

                var requestedCount = Math.Min(childCount, maxChildren);
                if (requestedCount <= 0)
                {
                    return result;
                }

                var rawChildren = new object[requestedCount];
                int obtained;
                var childrenResult = AccessibleChildren(
                    accessible,
                    0,
                    requestedCount,
                    rawChildren,
                    out obtained);

                if (childrenResult != 0 && childrenResult != 1)
                {
                    return result;
                }

                var children = new List<MsaaNode>();
                for (var index = 0; index < obtained; index++)
                {
                    var child = rawChildren[index];
                    if (child is int)
                    {
                        children.Add(CreateNode(
                            accessible,
                            child,
                            "child-id",
                            null));
                    }
                    else
                    {
                        var childAccessible = child as IAccessible;
                        if (childAccessible != null)
                        {
                            children.Add(CreateNode(
                                childAccessible,
                                0,
                                "accessible",
                                SafeChildCount(childAccessible)));
                        }
                        else
                        {
                            children.Add(new MsaaNode
                            {
                                Kind = "unknown",
                                Role = "unavailable",
                                State = "unavailable",
                                NameLength = -1,
                                ChildCount = null
                            });
                        }
                    }
                }

                result.Children = children.ToArray();
            }
            catch (Exception exception)
            {
                result.Available = false;
                result.ErrorType = exception.GetType().Name;
            }

            return result;
        }

        private static MsaaNode CreateNode(
            IAccessible accessible,
            object childId,
            string kind,
            int? childCount)
        {
            return new MsaaNode
            {
                Kind = kind,
                Role = SafeVariant(() => accessible.get_accRole(childId)),
                State = SafeVariant(() => accessible.get_accState(childId)),
                NameLength = SafeNameLength(accessible, childId),
                ChildCount = childCount
            };
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

        private static int SafeNameLength(
            IAccessible accessible,
            object childId)
        {
            try
            {
                var value = accessible.get_accName(childId);
                return value == null ? 0 : value.Length;
            }
            catch
            {
                return -1;
            }
        }

        private static string SafeVariant(Func<object> getter)
        {
            try
            {
                var value = getter();
                if (value is int)
                {
                    return ((int)value).ToString();
                }

                return value == null ? "unavailable" : "<non-numeric>";
            }
            catch
            {
                return "unavailable";
            }
        }
    }
}
'@ -ReferencedAssemblies Accessibility
}

function Get-LengthBucket {
    param([int]$Length)

    if ($Length -lt 0) { return 'unavailable' }
    if ($Length -eq 0) { return 'empty' }
    if ($Length -le 4) { return '1-4' }
    if ($Length -le 16) { return '5-16' }
    if ($Length -le 64) { return '17-64' }
    if ($Length -le 256) { return '65-256' }
    return '257+'
}

function Convert-Node {
    param([Sentory.Diagnostics.MsaaNode]$Node)

    if ($null -eq $Node) {
        return $null
    }

    [pscustomobject]@{
        kind = $Node.Kind
        role = $Node.Role
        state = $Node.State
        nameLengthBucket = Get-LengthBucket $Node.NameLength
        childCount = $Node.ChildCount
    }
}

$results = @()
foreach ($handleText in $Handles.Split(',')) {
    $trimmed = $handleText.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        continue
    }

    $numericText = if ($trimmed.StartsWith('0x')) {
        $trimmed.Substring(2)
    }
    else {
        $trimmed
    }
    $handle = [IntPtr][Convert]::ToInt64($numericText, 16)
    $probe = [Sentory.Diagnostics.MsaaNative]::Probe(
        $handle,
        $MaxChildren
    )

    $children = @(
        foreach ($child in $probe.Children) {
            Convert-Node $child
        }
    )

    $results += [pscustomobject]@{
        handle = $trimmed
        resultCode = $probe.ResultCode
        available = $probe.Available
        root = Convert-Node $probe.Root
        children = $children
        errorType = $probe.ErrorType
        warning = if (
            $null -ne $probe.Root -and
            $probe.Root.ChildCount -gt $MaxChildren
        ) {
            'max-children limit reached'
        }
        else {
            ''
        }
    }
}

[pscustomobject]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    privacyMode = 'MSAA names are reduced to length buckets; no names or values are emitted.'
    results = @($results)
} | ConvertTo-Json -Depth 7 -Compress
