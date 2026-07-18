using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Accessibility;
using Sentory.Core;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Interop;

public static class DiscordAccessibilityWorker
{
    private const uint ObjectIdClient = 0xFFFFFFFC;
    private const uint GetAncestorRoot = 2;
    private const int RoleSystemList = 33;
    private const int RoleSystemListItem = 34;
    private const int RoleSystemText = 42;
    private const int MessageListState = 1_048_640;
    private const int VisibleListItemState = 64;
    private const int MaximumTraversalDepth = 60;
    private const int MaximumTraversalNodes = 5_000;
    private static readonly Guid AccessibleInterfaceId =
        new("618736e0-3c3d-11cf-810c-00aa00389b71");

    public static async Task<int> RunAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        DiscordConfirmationResponse response;
        try
        {
            var json = await input.ReadLineAsync(cancellationToken);
            var request = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<DiscordConfirmationRequest>(json);
            response = request is null
                ? DiscordConfirmationResponse.Unavailable()
                : await ConfirmAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            response = DiscordConfirmationResponse.Unavailable();
        }

        await output.WriteLineAsync(JsonSerializer.Serialize(response));
        await output.FlushAsync(cancellationToken);
        return 0;
    }

    private static async Task<DiscordConfirmationResponse> ConfirmAsync(
        DiscordConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRequest(request, out var expectedUrls) ||
            !TryCreateAccessible(
                new nint(request.RendererWindowHandle),
                out var accessibleRoot))
        {
            return DiscordConfirmationResponse.Unavailable();
        }

        var targets = FindTargets(accessibleRoot, expectedUrls);
        if (targets.InputCandidates.Count != 1 ||
            targets.MessageLists.Count == 0)
        {
            return DiscordConfirmationResponse.Unavailable();
        }

        var inputTarget = targets.InputCandidates[0];
        var messageList = targets.MessageLists
            .OrderByDescending(target => GetDirectListItems(target).Count)
            .First();
        var baselineMessageCount = GetDirectListItems(messageList).Count;
        var timeout = TimeSpan.FromMilliseconds(
            Math.Clamp(request.TimeoutMilliseconds, 1_000, 300_000));
        var startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? inputEmptySince = null;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            await Task.Delay(180, cancellationToken);
            var contextValid = IsContextValid(request);
            var inputValue = SafeValue(
                inputTarget.Accessible,
                inputTarget.ChildId);
            var inputContains = ContainsAllUrls(inputValue, expectedUrls);
            var inputIsEmpty = IsEmptyInput(inputValue);
            var messages = GetDirectListItems(messageList);
            var latestMatches =
                messages.Count > baselineMessageCount &&
                SafeState(
                    messages[^1].Accessible,
                    messages[^1].ChildId) == VisibleListItemState &&
                SubtreeContainsAllUrls(messages[^1], expectedUrls);
            var decision = DiscordConfirmationEvaluator.Evaluate(
                baselineMessageCount,
                new DiscordCandidateObservation(
                    contextValid,
                    inputContains,
                    inputIsEmpty,
                    messages.Count,
                    latestMatches));

            if (decision == DiscordCandidateDecision.Confirmed)
            {
                return new DiscordConfirmationResponse(
                    DiscordConfirmationOutcome.Confirmed,
                    DateTimeOffset.UtcNow,
                    [
                        "discord-process-and-window",
                        "msaa-input-url-match",
                        "input-cleared-after-send",
                        "direct-message-list-item-increased",
                        "newest-message-url-match"
                    ]);
            }

            if (decision == DiscordCandidateDecision.Cancelled)
            {
                return new DiscordConfirmationResponse(
                    DiscordConfirmationOutcome.Cancelled,
                    null,
                    []);
            }

            if (inputIsEmpty)
            {
                inputEmptySince ??= DateTimeOffset.UtcNow;
                if (DateTimeOffset.UtcNow - inputEmptySince >=
                    TimeSpan.FromSeconds(5))
                {
                    return new DiscordConfirmationResponse(
                        DiscordConfirmationOutcome.Cancelled,
                        null,
                        []);
                }
            }
            else
            {
                inputEmptySince = null;
            }
        }

        return new DiscordConfirmationResponse(
            DiscordConfirmationOutcome.Expired,
            null,
            []);
    }

    private static bool TryValidateRequest(
        DiscordConfirmationRequest request,
        out HashSet<string> expectedUrls)
    {
        expectedUrls = [];
        if (request.MainWindowHandle == 0 ||
            request.RendererWindowHandle == 0 ||
            request.ProcessId == 0 ||
            request.NormalizedUrls.Count is < 1 or > 20)
        {
            return false;
        }

        foreach (var value in request.NormalizedUrls)
        {
            if (value.Length > 4_096 ||
                !UrlNormalizer.TryNormalize(value, out var normalized) ||
                !string.Equals(
                    normalized.Value,
                    value,
                    StringComparison.Ordinal))
            {
                return false;
            }

            expectedUrls.Add(value);
        }

        if (expectedUrls.Count == 0 || !IsContextValid(request))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(
                checked((int)request.ProcessId));
            return string.Equals(
                process.ProcessName,
                DiscordContextValidator.DiscordProcessName,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsContextValid(DiscordConfirmationRequest request)
    {
        var mainWindow = new nint(request.MainWindowHandle);
        var rendererWindow = new nint(request.RendererWindowHandle);
        if (!IsWindow(mainWindow) || !IsWindow(rendererWindow) ||
            GetAncestor(rendererWindow, GetAncestorRoot) != mainWindow)
        {
            return false;
        }

        GetWindowThreadProcessId(mainWindow, out var mainProcessId);
        GetWindowThreadProcessId(rendererWindow, out var rendererProcessId);
        return mainProcessId == request.ProcessId &&
               rendererProcessId == request.ProcessId &&
               string.Equals(
                   GetWindowClass(mainWindow),
                   DiscordContextValidator.MainWindowClassName,
                   StringComparison.Ordinal) &&
               string.Equals(
                   GetWindowClass(rendererWindow),
                   DiscordContextValidator.RendererClassName,
                   StringComparison.Ordinal);
    }

    private static TargetSearchResult FindTargets(
        IAccessible root,
        IReadOnlySet<string> expectedUrls)
    {
        var result = new TargetSearchResult();
        var visited = new HashSet<long>();
        var nodeCount = 0;
        Traverse(
            new AccessibleTarget(root, 0),
            expectedUrls,
            result,
            visited,
            ref nodeCount,
            0);
        return result;
    }

    private static void Traverse(
        AccessibleTarget target,
        IReadOnlySet<string> expectedUrls,
        TargetSearchResult result,
        ISet<long> visited,
        ref int nodeCount,
        int depth)
    {
        if (depth > MaximumTraversalDepth ||
            nodeCount++ >= MaximumTraversalNodes)
        {
            return;
        }

        var role = SafeRole(target.Accessible, target.ChildId);
        if (role == RoleSystemText &&
            ContainsAllUrls(
                SafeValue(target.Accessible, target.ChildId),
                expectedUrls))
        {
            result.InputCandidates.Add(target);
        }

        if (role == RoleSystemList &&
            SafeState(target.Accessible, target.ChildId) == MessageListState)
        {
            result.MessageLists.Add(target);
        }

        var nested = ToAccessible(target);
        if (nested is null || !MarkVisited(nested, visited))
        {
            return;
        }

        foreach (var child in GetChildren(nested))
        {
            Traverse(
                child,
                expectedUrls,
                result,
                visited,
                ref nodeCount,
                depth + 1);
        }
    }

    private static bool SubtreeContainsAllUrls(
        AccessibleTarget root,
        IReadOnlySet<string> expectedUrls)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<long>();
        var nodeCount = 0;

        void Inspect(AccessibleTarget target, int depth)
        {
            if (depth > MaximumTraversalDepth ||
                nodeCount++ >= MaximumTraversalNodes ||
                found.Count == expectedUrls.Count)
            {
                return;
            }

            AddUrls(SafeName(target.Accessible, target.ChildId), found);
            AddUrls(SafeValue(target.Accessible, target.ChildId), found);
            var nested = ToAccessible(target);
            if (nested is null || !MarkVisited(nested, visited))
            {
                return;
            }

            foreach (var child in GetChildren(nested))
            {
                Inspect(child, depth + 1);
            }
        }

        Inspect(root, 0);
        return expectedUrls.All(found.Contains);
    }

    private static List<AccessibleTarget> GetDirectListItems(
        AccessibleTarget list) =>
        GetChildren(ToAccessible(list))
            .Where(child =>
                SafeRole(child.Accessible, child.ChildId) ==
                RoleSystemListItem)
            .ToList();

    private static List<AccessibleTarget> GetChildren(IAccessible? container)
    {
        var results = new List<AccessibleTarget>();
        if (container is null)
        {
            return results;
        }

        var childCount = SafeChildCount(container);
        if (childCount <= 0 || childCount > MaximumTraversalNodes)
        {
            return results;
        }

        var children = new object[childCount];
        var resultCode = AccessibleChildren(
            container,
            0,
            childCount,
            children,
            out var obtained);
        if (resultCode is not 0 and not 1)
        {
            return results;
        }

        for (var index = 0; index < obtained; index++)
        {
            if (children[index] is int childId)
            {
                results.Add(new AccessibleTarget(container, childId));
            }
            else if (children[index] is IAccessible accessible)
            {
                results.Add(new AccessibleTarget(accessible, 0));
            }
        }

        return results;
    }

    private static bool TryCreateAccessible(
        nint window,
        out IAccessible accessible)
    {
        accessible = null!;
        var interfaceId = AccessibleInterfaceId;
        var result = AccessibleObjectFromWindow(
            window,
            ObjectIdClient,
            ref interfaceId,
            out var raw);
        if (result != 0 || raw is not IAccessible value)
        {
            return false;
        }

        accessible = value;
        return true;
    }

    private static IAccessible? ToAccessible(AccessibleTarget target)
    {
        if (target.ChildId is int childId && childId == 0)
        {
            return target.Accessible;
        }

        try
        {
            return target.Accessible.get_accChild(target.ChildId)
                as IAccessible;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static bool ContainsAllUrls(
        string? value,
        IReadOnlySet<string> expectedUrls)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var urls = UrlExtractor.Extract(value)
            .Select(url => url.Value)
            .ToHashSet(StringComparer.Ordinal);
        return expectedUrls.All(urls.Contains);
    }

    private static void AddUrls(string? value, ISet<string> destination)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var url in UrlExtractor.Extract(value))
        {
            destination.Add(url.Value);
        }
    }

    private static bool IsEmptyInput(string? value) =>
        string.IsNullOrWhiteSpace(
            value?.Trim('\r', '\n', '\u200B', '\uFEFF'));

    private static int SafeRole(IAccessible accessible, object childId)
    {
        try
        {
            return accessible.get_accRole(childId) is int role ? role : -1;
        }
        catch (COMException)
        {
            return -1;
        }
    }

    private static int SafeState(IAccessible accessible, object childId)
    {
        try
        {
            return accessible.get_accState(childId) is int state ? state : -1;
        }
        catch (COMException)
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
        catch (COMException)
        {
            return 0;
        }
    }

    private static string? SafeName(IAccessible accessible, object childId)
    {
        try
        {
            return accessible.get_accName(childId);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? SafeValue(IAccessible accessible, object childId)
    {
        try
        {
            return accessible.get_accValue(childId);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static bool MarkVisited(
        IAccessible accessible,
        ISet<long> visited)
    {
        nint unknown = nint.Zero;
        try
        {
            unknown = Marshal.GetIUnknownForObject(accessible);
            return visited.Add(unknown.ToInt64());
        }
        catch (COMException)
        {
            return true;
        }
        finally
        {
            if (unknown != nint.Zero)
            {
                Marshal.Release(unknown);
            }
        }
    }

    private static string GetWindowClass(nint window)
    {
        var className = new StringBuilder(256);
        return GetClassName(window, className, className.Capacity) > 0
            ? className.ToString()
            : string.Empty;
    }

    private sealed record AccessibleTarget(
        IAccessible Accessible,
        object ChildId);

    private sealed class TargetSearchResult
    {
        public List<AccessibleTarget> InputCandidates { get; } = [];

        public List<AccessibleTarget> MessageLists { get; } = [];
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint window,
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint window,
        StringBuilder className,
        int maximumCount);
}
