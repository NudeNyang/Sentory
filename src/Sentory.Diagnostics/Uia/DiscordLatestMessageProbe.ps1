param(
    [Parameter(Mandatory = $true)]
    [string]$RendererHandle,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedText
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

Add-Type -AssemblyName Accessibility

if (-not ('Sentory.Diagnostics.DiscordLatestMessageNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Accessibility;

namespace Sentory.Diagnostics
{
    public sealed class DiscordLatestMessageResult
    {
        public bool Available { get; set; }
        public int MessageListChildCount { get; set; }
        public int DirectListItemCount { get; set; }
        public int VisibleListItemCount { get; set; }
        public string LatestItemState { get; set; }
        public int LatestSubtreeNodeCount { get; set; }
        public int NameExactMatches { get; set; }
        public int NameContainsMatches { get; set; }
        public int ValueExactMatches { get; set; }
        public int ValueContainsMatches { get; set; }
        public string ErrorType { get; set; }
    }

    public static class DiscordLatestMessageNative
    {
        private const uint ObjectIdClient = 0xFFFFFFFC;
        private const int RoleSystemList = 33;
        private const int RoleSystemListItem = 34;
        private const int MessageListState = 1048640;
        private const int VisibleListItemState = 64;
        private static readonly Guid AccessibleInterfaceId =
            new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");

        private sealed class Target
        {
            public IAccessible Accessible;
            public object ChildId;
            public int ChildCount;
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

        public static DiscordLatestMessageResult Probe(
            IntPtr rendererHandle,
            string expectedText)
        {
            var result = new DiscordLatestMessageResult
            {
                LatestItemState = "unavailable",
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

                var root = (IAccessible)rawAccessible;
                var lists = new List<Target>();
                var visited = new HashSet<long>();
                MarkVisited(root, visited);
                FindMessageLists(root, lists, visited, 0);

                Target messageList = null;
                foreach (var candidate in lists)
                {
                    if (
                        messageList == null ||
                        candidate.ChildCount > messageList.ChildCount)
                    {
                        messageList = candidate;
                    }
                }

                if (messageList == null)
                {
                    result.ErrorType = "MessageListNotFound";
                    return result;
                }

                result.MessageListChildCount = messageList.ChildCount;
                var directChildren = GetChildren(
                    messageList.Accessible,
                    messageList.ChildId);
                Target latestListItem = null;

                foreach (var child in directChildren)
                {
                    if (
                        SafeRole(child.Accessible, child.ChildId) !=
                        RoleSystemListItem)
                    {
                        continue;
                    }

                    result.DirectListItemCount++;
                    var state = SafeState(
                        child.Accessible,
                        child.ChildId);
                    if (state == VisibleListItemState)
                    {
                        result.VisibleListItemCount++;
                    }

                    latestListItem = child;
                }

                if (latestListItem == null)
                {
                    result.ErrorType = "LatestListItemNotFound";
                    return result;
                }

                result.LatestItemState = SafeState(
                    latestListItem.Accessible,
                    latestListItem.ChildId).ToString();
                InspectSubtree(
                    latestListItem,
                    expectedText,
                    result,
                    new HashSet<long>(),
                    0);
                result.Available = true;
            }
            catch (Exception exception)
            {
                result.ErrorType = exception.GetType().Name;
            }

            return result;
        }

        private static void FindMessageLists(
            IAccessible container,
            IList<Target> lists,
            ISet<long> visited,
            int depth)
        {
            if (depth > 60)
            {
                return;
            }

            foreach (var child in GetChildren(container, 0))
            {
                var role = SafeRole(child.Accessible, child.ChildId);
                var state = SafeState(child.Accessible, child.ChildId);
                if (
                    role == RoleSystemList &&
                    state == MessageListState)
                {
                    child.ChildCount = SafeChildCount(
                        child.Accessible,
                        child.ChildId);
                    lists.Add(child);
                }

                var nested = ToAccessible(child);
                if (
                    nested != null &&
                    MarkVisited(nested, visited))
                {
                    FindMessageLists(
                        nested,
                        lists,
                        visited,
                        depth + 1);
                }
            }
        }

        private static void InspectSubtree(
            Target target,
            string expectedText,
            DiscordLatestMessageResult result,
            ISet<long> visited,
            int depth)
        {
            if (depth > 60)
            {
                return;
            }

            result.LatestSubtreeNodeCount++;
            InspectText(
                SafeName(target.Accessible, target.ChildId),
                expectedText,
                true,
                result);
            InspectText(
                SafeValue(target.Accessible, target.ChildId),
                expectedText,
                false,
                result);

            var nested = ToAccessible(target);
            if (nested == null || !MarkVisited(nested, visited))
            {
                return;
            }

            foreach (var child in GetChildren(nested, 0))
            {
                InspectSubtree(
                    child,
                    expectedText,
                    result,
                    visited,
                    depth + 1);
            }
        }

        private static void InspectText(
            string value,
            string expectedText,
            bool isName,
            DiscordLatestMessageResult result)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var normalized = value.TrimEnd('\r', '\n');
            var exact = string.Equals(
                normalized,
                expectedText,
                StringComparison.Ordinal);
            var contains = normalized.IndexOf(
                expectedText,
                StringComparison.Ordinal) >= 0;

            if (isName)
            {
                if (exact) result.NameExactMatches++;
                if (contains) result.NameContainsMatches++;
            }
            else
            {
                if (exact) result.ValueExactMatches++;
                if (contains) result.ValueContainsMatches++;
            }
        }

        private static List<Target> GetChildren(
            IAccessible container,
            object childId)
        {
            var targetContainer = childId is int && (int)childId == 0
                ? container
                : ToAccessible(new Target
                {
                    Accessible = container,
                    ChildId = childId
                });
            var results = new List<Target>();
            if (targetContainer == null)
            {
                return results;
            }

            var childCount = SafeChildCount(targetContainer, 0);
            if (childCount <= 0)
            {
                return results;
            }

            var rawChildren = new object[childCount];
            int obtained;
            var childrenResult = AccessibleChildren(
                targetContainer,
                0,
                childCount,
                rawChildren,
                out obtained);
            if (childrenResult != 0 && childrenResult != 1)
            {
                return results;
            }

            for (var index = 0; index < obtained; index++)
            {
                var rawChild = rawChildren[index];
                if (rawChild is int)
                {
                    results.Add(new Target
                    {
                        Accessible = targetContainer,
                        ChildId = rawChild
                    });
                }
                else
                {
                    var accessible = rawChild as IAccessible;
                    if (accessible != null)
                    {
                        results.Add(new Target
                        {
                            Accessible = accessible,
                            ChildId = 0
                        });
                    }
                }
            }

            return results;
        }

        private static IAccessible ToAccessible(Target target)
        {
            if (target.ChildId is int && (int)target.ChildId == 0)
            {
                return target.Accessible;
            }

            try
            {
                return target.Accessible.get_accChild(
                    target.ChildId) as IAccessible;
            }
            catch
            {
                return null;
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

        private static int SafeState(
            IAccessible accessible,
            object childId)
        {
            try
            {
                var state = accessible.get_accState(childId);
                return state is int ? (int)state : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeChildCount(
            IAccessible accessible,
            object childId)
        {
            var nested = childId is int && (int)childId == 0
                ? accessible
                : ToAccessible(new Target
                {
                    Accessible = accessible,
                    ChildId = childId
                });
            if (nested == null)
            {
                return 0;
            }

            try
            {
                return nested.accChildCount;
            }
            catch
            {
                return 0;
            }
        }

        private static string SafeName(
            IAccessible accessible,
            object childId)
        {
            try
            {
                return accessible.get_accName(childId);
            }
            catch
            {
                return null;
            }
        }

        private static string SafeValue(
            IAccessible accessible,
            object childId)
        {
            try
            {
                return accessible.get_accValue(childId);
            }
            catch
            {
                return null;
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

$result = [Sentory.Diagnostics.DiscordLatestMessageNative]::Probe(
    (Convert-Handle $RendererHandle),
    $ExpectedText
)

[pscustomobject]@{
    available = $result.Available
    messageListChildCount = $result.MessageListChildCount
    directListItemCount = $result.DirectListItemCount
    visibleListItemCount = $result.VisibleListItemCount
    latestItemState = $result.LatestItemState
    latestSubtreeNodeCount = $result.LatestSubtreeNodeCount
    nameExactMatches = $result.NameExactMatches
    nameContainsMatches = $result.NameContainsMatches
    valueExactMatches = $result.ValueExactMatches
    valueContainsMatches = $result.ValueContainsMatches
    expectedContentConfirmed = (
        $result.NameExactMatches -gt 0 -or
        $result.NameContainsMatches -gt 0 -or
        $result.ValueExactMatches -gt 0 -or
        $result.ValueContainsMatches -gt 0
    )
    errorType = $result.ErrorType
    privacyMode = 'Only the newest list item subtree is checked against the caller-provided expected URL. Raw text is not emitted.'
} | ConvertTo-Json -Depth 4 -Compress
