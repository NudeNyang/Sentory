param(
    [Parameter(Mandatory = $true)]
    [string]$Handles,

    [ValidateRange(1, 50000)]
    [int]$MaxNodes = 10000,

    [ValidateRange(1, 100)]
    [int]$MaxDepth = 30
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

Add-Type -AssemblyName Accessibility

if (-not ('Sentory.Diagnostics.MsaaTreeNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Accessibility;

namespace Sentory.Diagnostics
{
    public sealed class MsaaTreeNode
    {
        public int Index { get; set; }
        public int ParentIndex { get; set; }
        public int Depth { get; set; }
        public string Kind { get; set; }
        public string Role { get; set; }
        public string State { get; set; }
        public int NameLength { get; set; }
        public int ChildCount { get; set; }
    }

    public sealed class MsaaTreeResult
    {
        public int ResultCode { get; set; }
        public bool Available { get; set; }
        public MsaaTreeNode[] Nodes { get; set; }
        public bool Truncated { get; set; }
        public string ErrorType { get; set; }
    }

    public static class MsaaTreeNative
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

        public static MsaaTreeResult Probe(
            IntPtr windowHandle,
            int maxNodes,
            int maxDepth)
        {
            var result = new MsaaTreeResult
            {
                Nodes = new MsaaTreeNode[0],
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

                var root = (IAccessible)rawAccessible;
                result.Available = true;

                var nodes = new List<MsaaTreeNode>();
                var visited = new HashSet<long>();
                MarkVisited(root, visited);

                nodes.Add(CreateNode(
                    root,
                    0,
                    0,
                    -1,
                    0,
                    "root"));

                Traverse(
                    root,
                    0,
                    1,
                    nodes,
                    visited,
                    maxNodes,
                    maxDepth,
                    result);

                result.Nodes = nodes.ToArray();
            }
            catch (Exception exception)
            {
                result.Available = false;
                result.ErrorType = exception.GetType().Name;
            }

            return result;
        }

        private static void Traverse(
            IAccessible container,
            int parentIndex,
            int depth,
            List<MsaaTreeNode> nodes,
            HashSet<long> visited,
            int maxNodes,
            int maxDepth,
            MsaaTreeResult result)
        {
            if (depth > maxDepth)
            {
                result.Truncated = true;
                return;
            }

            var childCount = SafeChildCount(container);
            if (childCount <= 0)
            {
                return;
            }

            var remaining = maxNodes - nodes.Count;
            if (remaining <= 0)
            {
                result.Truncated = true;
                return;
            }

            var requested = Math.Min(childCount, remaining);
            var rawChildren = new object[requested];
            int obtained;
            var childrenResult = AccessibleChildren(
                container,
                0,
                requested,
                rawChildren,
                out obtained);

            if (childrenResult != 0 && childrenResult != 1)
            {
                return;
            }

            for (var position = 0; position < obtained; position++)
            {
                if (nodes.Count >= maxNodes)
                {
                    result.Truncated = true;
                    return;
                }

                var rawChild = rawChildren[position];
                IAccessible childAccessible = null;
                object childId = 0;
                var kind = "unknown";

                if (rawChild is int)
                {
                    childId = rawChild;
                    kind = "child-id";
                    try
                    {
                        childAccessible =
                            container.get_accChild(rawChild) as IAccessible;
                    }
                    catch
                    {
                        childAccessible = null;
                    }
                }
                else
                {
                    childAccessible = rawChild as IAccessible;
                    kind = childAccessible == null
                        ? "unknown"
                        : "accessible";
                }

                var index = nodes.Count;
                if (childAccessible != null)
                {
                    nodes.Add(CreateNode(
                        childAccessible,
                        0,
                        index,
                        parentIndex,
                        depth,
                        kind));

                    if (MarkVisited(childAccessible, visited))
                    {
                        Traverse(
                            childAccessible,
                            index,
                            depth + 1,
                            nodes,
                            visited,
                            maxNodes,
                            maxDepth,
                            result);
                    }
                }
                else
                {
                    nodes.Add(CreateNode(
                        container,
                        childId,
                        index,
                        parentIndex,
                        depth,
                        kind));
                }
            }

            if (requested < childCount)
            {
                result.Truncated = true;
            }
        }

        private static MsaaTreeNode CreateNode(
            IAccessible accessible,
            object childId,
            int index,
            int parentIndex,
            int depth,
            string kind)
        {
            var childCount = childId is int && (int)childId == 0
                ? SafeChildCount(accessible)
                : 0;

            return new MsaaTreeNode
            {
                Index = index,
                ParentIndex = parentIndex,
                Depth = depth,
                Kind = kind,
                Role = SafeVariant(() => accessible.get_accRole(childId)),
                State = SafeVariant(() => accessible.get_accState(childId)),
                NameLength = SafeNameLength(accessible, childId),
                ChildCount = childCount
            };
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
    $probe = [Sentory.Diagnostics.MsaaTreeNative]::Probe(
        $handle,
        $MaxNodes,
        $MaxDepth
    )

    $nodes = @(
        foreach ($node in $probe.Nodes) {
            [pscustomobject]@{
                index = $node.Index
                parentIndex = $node.ParentIndex
                depth = $node.Depth
                kind = $node.Kind
                role = $node.Role
                state = $node.State
                nameLengthBucket = Get-LengthBucket $node.NameLength
                childCount = $node.ChildCount
            }
        }
    )

    $results += [pscustomobject]@{
        handle = $trimmed
        resultCode = $probe.ResultCode
        available = $probe.Available
        truncated = $probe.Truncated
        errorType = $probe.ErrorType
        nodeCount = $nodes.Count
        nodes = $nodes
    }
}

[pscustomobject]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    privacyMode = 'MSAA names are reduced to length buckets; no names or values are emitted.'
    results = @($results)
} | ConvertTo-Json -Depth 7 -Compress
