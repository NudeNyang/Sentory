using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    private const int RoleSystemDocument = 15;
    private const int RoleSystemList = 33;
    private const int RoleSystemListItem = 34;
    private const int RoleSystemGraphic = 40;
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
        var activeRequests =
            new ConcurrentDictionary<Guid, CancellationTokenSource>();
        var pendingResponses = new List<Task>();
        var targetCache = new WorkerTargetCache();
        using var outputGate = new SemaphoreSlim(1, 1);

        while (true)
        {
            var json = await input.ReadLineAsync(cancellationToken);
            if (json is null)
            {
                break;
            }

            DiscordWorkerMessage? message;
            try
            {
                message = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<DiscordWorkerMessage>(json);
            }
            catch (JsonException exception)
            {
                await WriteResponseAsync(
                    output,
                    outputGate,
                    new DiscordWorkerResponse(
                        Guid.Empty,
                        DiscordConfirmationResponse.Unavailable(
                            $"worker-json:{exception.GetType().Name}")),
                    cancellationToken);
                continue;
            }

            if (message?.Operation == DiscordWorkerOperation.Cancel)
            {
                if (activeRequests.TryGetValue(
                        message.RequestId,
                        out var activeRequest))
                {
                    activeRequest.Cancel();
                }

                continue;
            }

            if (message?.Operation != DiscordWorkerOperation.Confirm ||
                message.Request is null ||
                message.RequestId == Guid.Empty)
            {
                await WriteResponseAsync(
                    output,
                    outputGate,
                    new DiscordWorkerResponse(
                        message?.RequestId ?? Guid.Empty,
                        DiscordConfirmationResponse.Unavailable(
                            "worker-request-invalid")),
                    cancellationToken);
                continue;
            }

            var requestCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            if (!activeRequests.TryAdd(
                    message.RequestId,
                    requestCancellation))
            {
                requestCancellation.Dispose();
                await WriteResponseAsync(
                    output,
                    outputGate,
                    new DiscordWorkerResponse(
                        message.RequestId,
                        DiscordConfirmationResponse.Unavailable(
                            "worker-request-id-duplicate")),
                    cancellationToken);
                continue;
            }

            pendingResponses.RemoveAll(task => task.IsCompleted);
            pendingResponses.Add(ProcessRequestAsync(
                message,
                requestCancellation,
                activeRequests,
                targetCache,
                output,
                outputGate,
                cancellationToken));
        }

        foreach (var request in activeRequests.Values)
        {
            request.Cancel();
        }

        await Task.WhenAll(pendingResponses);

        return 0;
    }

    private static async Task ProcessRequestAsync(
        DiscordWorkerMessage message,
        CancellationTokenSource requestCancellation,
        ConcurrentDictionary<Guid, CancellationTokenSource> activeRequests,
        WorkerTargetCache targetCache,
        TextWriter output,
        SemaphoreSlim outputGate,
        CancellationToken workerCancellation)
    {
        DiscordConfirmationResponse response;
        try
        {
            response = await ConfirmAsync(
                message.Request!,
                targetCache,
                requestCancellation.Token);
        }
        catch (OperationCanceledException)
            when (requestCancellation.IsCancellationRequested)
        {
            response = new DiscordConfirmationResponse(
                DiscordConfirmationOutcome.Cancelled,
                null,
                ["worker-request-cancelled"]);
        }
        catch (Exception exception)
        {
            response = DiscordConfirmationResponse.Unavailable(
                $"worker-exception:{exception.GetType().Name}");
        }
        finally
        {
            activeRequests.TryRemove(message.RequestId, out _);
            requestCancellation.Dispose();
        }

        if (!workerCancellation.IsCancellationRequested)
        {
            await WriteResponseAsync(
                output,
                outputGate,
                new DiscordWorkerResponse(message.RequestId, response),
                workerCancellation);
        }
    }

    private static async Task WriteResponseAsync(
        TextWriter output,
        SemaphoreSlim outputGate,
        DiscordWorkerResponse response,
        CancellationToken cancellationToken)
    {
        await outputGate.WaitAsync(cancellationToken);
        try
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(response));
            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            outputGate.Release();
        }
    }

    private static async Task<DiscordConfirmationResponse> ConfirmAsync(
        DiscordConfirmationRequest request,
        WorkerTargetCache targetCache,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRequest(request, out var expectedUrls))
        {
            return DiscordConfirmationResponse.Unavailable(
                "request-or-window-validation-failed");
        }

        var requireMatchingUrlInput = RequiresMatchingUrlInput(request);
        if (!TryResolveTargets(
                request,
                expectedUrls,
                requireMatchingUrlInput,
                targetCache,
                out var resolved,
                out var unavailableSignal))
        {
            return DiscordConfirmationResponse.Unavailable(
                unavailableSignal);
        }

        if (request.ContentKind == DiscordConfirmationContentKind.Warmup)
        {
            return new DiscordConfirmationResponse(
                DiscordConfirmationOutcome.Confirmed,
                DateTimeOffset.UtcNow,
                [resolved.CacheHit ? "target-cache-hit" : "target-cache-warmed"]);
        }

        var accessibleRoot = resolved.AccessibleRoot;
        var messageList = resolved.MessageList;
        var baselineMessages = GetDirectListItems(messageList);
        var baselineMessageCount = baselineMessages.Count;
        var baselineFingerprints = CreateMessageFingerprintSet(
            baselineMessages);
        if (request.ContentKind == DiscordConfirmationContentKind.Image)
        {
            if (request.ExplicitSendObserved &&
                baselineMessages.Count > 0 &&
                IsVisibleOwnedImageMessage(baselineMessages[^1]))
            {
                return CreateConfirmedImageResponse(
                    DateTimeOffset.UtcNow,
                    "send-key-and-current-message-match",
                    resolved.CacheHit);
            }

            ExcludeLatestFromBaselineWhenSendWasObserved(
                request,
                baselineMessages,
                baselineFingerprints);
            return await ConfirmImageAsync(
                request,
                accessibleRoot,
                messageList,
                baselineMessageCount,
                baselineFingerprints,
                resolved.CacheHit,
                cancellationToken);
        }

        var inputTarget = requireMatchingUrlInput
            ? resolved.InputTarget
            : null;
        if (request.ExplicitSendObserved &&
            baselineMessages.Count > 0 &&
            IsVisibleUrlMessage(baselineMessages[^1], expectedUrls))
        {
            return CreateConfirmedUrlResponse(
                DateTimeOffset.UtcNow,
                "send-key-and-current-message-match",
                request.ExplicitSendObserved,
                resolved.CacheHit);
        }

        ExcludeLatestFromBaselineWhenSendWasObserved(
            request,
            baselineMessages,
            baselineFingerprints);
        var timeout = TimeSpan.FromMilliseconds(
            Math.Clamp(request.TimeoutMilliseconds, 1_000, 300_000));
        var startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? inputEmptySince = null;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            await Task.Delay(180, cancellationToken);
            var contextValid = IsContextValid(request);
            var inputValue = inputTarget is null
                ? null
                : SafeValue(
                    inputTarget.Accessible,
                    inputTarget.ChildId);
            var inputContains = ContainsAllUrls(inputValue, expectedUrls);
            var inputIsEmpty = IsEmptyInput(inputValue);
            var messages = GetMessagesWithRefresh(
                accessibleRoot,
                ref messageList);
            var newMessages = GetNewMessages(
                messages,
                baselineMessageCount,
                baselineFingerprints);
            var matchingMessageFound = newMessages.Any(message =>
                IsVisibleUrlMessage(message, expectedUrls));
            var decision = DiscordConfirmationEvaluator.Evaluate(
                baselineMessageCount,
                new DiscordCandidateObservation(
                    contextValid,
                    inputContains,
                    inputIsEmpty,
                    newMessages.Count,
                    messages.Count,
                    matchingMessageFound));

            if (decision == DiscordCandidateDecision.Confirmed)
            {
                return CreateConfirmedUrlResponse(
                    DateTimeOffset.UtcNow,
                    "new-message-set-url-match",
                    request.ExplicitSendObserved,
                    resolved.CacheHit);
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

    private static async Task<DiscordConfirmationResponse> ConfirmImageAsync(
        DiscordConfirmationRequest request,
        IAccessible accessibleRoot,
        AccessibleTarget messageList,
        int baselineMessageCount,
        IReadOnlySet<string> baselineFingerprints,
        bool cacheHit,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(
            Math.Clamp(request.TimeoutMilliseconds, 1_000, 120_000));
        var startedAt = DateTimeOffset.UtcNow;
        var latestMessageCount = baselineMessageCount;
        var latestNewMessageCount = 0;
        var matchingOwnedImageFound = false;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            await Task.Delay(180, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var contextValid = IsContextValid(request);
            var messages = GetMessagesWithRefresh(
                accessibleRoot,
                ref messageList);
            latestMessageCount = messages.Count;
            var newMessages = GetNewMessages(
                messages,
                baselineMessageCount,
                baselineFingerprints);
            latestNewMessageCount = newMessages.Count;
            matchingOwnedImageFound = newMessages.Any(
                IsVisibleOwnedImageMessage);
            var decision = DiscordImageConfirmationEvaluator.Evaluate(
                baselineMessageCount,
                new DiscordImageCandidateObservation(
                    contextValid,
                    newMessages.Count,
                    messages.Count,
                    matchingOwnedImageFound));

            if (decision == DiscordCandidateDecision.Confirmed)
            {
                return CreateConfirmedImageResponse(
                    now,
                    "new-message-set-owned-image-match",
                    cacheHit);
            }

            if (decision == DiscordCandidateDecision.Cancelled)
            {
                return new DiscordConfirmationResponse(
                    DiscordConfirmationOutcome.Cancelled,
                    null,
                    BuildImageDiagnosticSignals(
                        baselineMessageCount,
                        latestMessageCount,
                        latestNewMessageCount,
                        matchingOwnedImageFound));
            }
        }

        return new DiscordConfirmationResponse(
            DiscordConfirmationOutcome.Expired,
            null,
            BuildImageDiagnosticSignals(
                baselineMessageCount,
                latestMessageCount,
                latestNewMessageCount,
                matchingOwnedImageFound));
    }

    private static IReadOnlyList<string> BuildImageDiagnosticSignals(
        int baselineMessageCount,
        int latestMessageCount,
        int newMessageCount,
        bool matchingOwnedImageFound) =>
        [
            $"baseline-count:{baselineMessageCount}",
            $"latest-count:{latestMessageCount}",
            $"new-message-count:{newMessageCount}",
            $"matching-owned-image:{matchingOwnedImageFound}"
        ];

    private static DiscordConfirmationResponse CreateConfirmedUrlResponse(
        DateTimeOffset confirmedAt,
        string correlationSignal,
        bool explicitSendObserved,
        bool cacheHit) =>
        new(
            DiscordConfirmationOutcome.Confirmed,
            confirmedAt,
            [
                "discord-process-and-window",
                explicitSendObserved
                    ? "validated-discord-send-key"
                    : "msaa-input-url-match",
                explicitSendObserved
                    ? "post-send-message-url-match"
                    : "input-cleared-after-send",
                cacheHit ? "target-cache-hit" : "target-cache-miss",
                correlationSignal
            ]);

    private static DiscordConfirmationResponse CreateConfirmedImageResponse(
        DateTimeOffset confirmedAt,
        string correlationSignal,
        bool cacheHit) =>
        new(
            DiscordConfirmationOutcome.Confirmed,
            confirmedAt,
            [
                "discord-process-and-window",
                "clipboard-image-paste-in-discord-input",
                cacheHit ? "target-cache-hit" : "target-cache-miss",
                correlationSignal
            ]);

    private static bool IsVisibleUrlMessage(
        AccessibleTarget message,
        IReadOnlySet<string> expectedUrls) =>
        SafeState(message.Accessible, message.ChildId) ==
            VisibleListItemState &&
        SubtreeContainsAllUrls(message, expectedUrls);

    private static bool IsVisibleOwnedImageMessage(
        AccessibleTarget message) =>
        SafeState(message.Accessible, message.ChildId) ==
            VisibleListItemState &&
        SubtreeContainsImageAttachment(message) &&
        SubtreeContainsOwnedAttachmentControl(message);

    private static List<AccessibleTarget> GetNewMessages(
        IReadOnlyList<AccessibleTarget> messages,
        int baselineMessageCount,
        IReadOnlySet<string> baselineFingerprints)
    {
        var newMessages = messages
            .Where(message =>
            {
                var fingerprint = CreateStableMessageFingerprint(message);
                return fingerprint is not null &&
                       !baselineFingerprints.Contains(fingerprint);
            })
            .ToList();
        if (newMessages.Count > 0 ||
            messages.Count <= baselineMessageCount)
        {
            return newMessages;
        }

        return messages
            .Skip(Math.Min(baselineMessageCount, messages.Count))
            .ToList();
    }

    private static HashSet<string> CreateMessageFingerprintSet(
        IEnumerable<AccessibleTarget> messages) =>
        messages
            .Select(CreateStableMessageFingerprint)
            .Where(fingerprint => fingerprint is not null)
            .Select(fingerprint => fingerprint!)
            .ToHashSet(StringComparer.Ordinal);

    private static void ExcludeLatestFromBaselineWhenSendWasObserved(
        DiscordConfirmationRequest request,
        IReadOnlyList<AccessibleTarget> baselineMessages,
        ISet<string> baselineFingerprints)
    {
        if (!request.ExplicitSendObserved || baselineMessages.Count == 0)
        {
            return;
        }

        var latest = CreateStableMessageFingerprint(baselineMessages[^1]);
        if (latest is not null)
        {
            baselineFingerprints.Remove(latest);
        }
    }

    private static string? CreateStableMessageFingerprint(
        AccessibleTarget root)
    {
        var visited = new HashSet<long>();
        var nodeCount = 0;
        string? signature = null;

        void Inspect(AccessibleTarget target, int depth)
        {
            if (signature is not null ||
                depth > MaximumTraversalDepth ||
                nodeCount++ >= MaximumTraversalNodes)
            {
                return;
            }

            if (SafeRole(target.Accessible, target.ChildId) ==
                RoleSystemDocument)
            {
                var name = SafeName(target.Accessible, target.ChildId);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    signature = name;
                    return;
                }
            }

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
        return signature is null
            ? null
            : Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
    }

    private static bool TryResolveTargets(
        DiscordConfirmationRequest request,
        IReadOnlySet<string> expectedUrls,
        bool requireMatchingUrlInput,
        WorkerTargetCache cache,
        out TargetResolution resolution,
        out string unavailableSignal)
    {
        resolution = null!;
        unavailableSignal = "message-list-unavailable";
        var windowTitle = GetWindowTitle(
            new nint(request.MainWindowHandle));

        if (cache.Matches(request, windowTitle) &&
            IsCachedMessageListUsable(cache.MessageList!))
        {
            if (!requireMatchingUrlInput)
            {
                resolution = new TargetResolution(
                    cache.AccessibleRoot!,
                    cache.MessageList!,
                    null,
                    true);
                return true;
            }

            if (cache.InputTarget is { } cachedInput &&
                SafeRole(cachedInput.Accessible, cachedInput.ChildId) ==
                    RoleSystemText &&
                ContainsAllUrls(
                    SafeValue(cachedInput.Accessible, cachedInput.ChildId),
                    expectedUrls))
            {
                resolution = new TargetResolution(
                    cache.AccessibleRoot!,
                    cache.MessageList!,
                    cachedInput,
                    true);
                return true;
            }

            var refreshed = FindTargets(
                cache.AccessibleRoot!,
                expectedUrls,
                requireMatchingUrlInput: true);
            if (TrySelectTargets(
                    refreshed,
                    requireMatchingUrlInput: true,
                    out var refreshedMessageList,
                    out var refreshedInput,
                    out unavailableSignal))
            {
                cache.Update(
                    request,
                    windowTitle,
                    cache.AccessibleRoot!,
                    refreshedMessageList,
                    refreshedInput);
                resolution = new TargetResolution(
                    cache.AccessibleRoot!,
                    refreshedMessageList,
                    refreshedInput,
                    false);
                return true;
            }

            cache.Clear();
        }

        if (!TryCreateAccessible(
                new nint(request.RendererWindowHandle),
                out var accessibleRoot))
        {
            unavailableSignal = "renderer-accessibility-root-unavailable";
            return false;
        }

        var targets = FindTargets(
            accessibleRoot,
            expectedUrls,
            requireMatchingUrlInput);
        if (!TrySelectTargets(
                targets,
                requireMatchingUrlInput,
                out var messageList,
                out var inputTarget,
                out unavailableSignal))
        {
            return false;
        }

        cache.Update(
            request,
            windowTitle,
            accessibleRoot,
            messageList,
            inputTarget);
        resolution = new TargetResolution(
            accessibleRoot,
            messageList,
            inputTarget,
            false);
        return true;
    }

    private static bool TrySelectTargets(
        TargetSearchResult targets,
        bool requireMatchingUrlInput,
        out AccessibleTarget messageList,
        out AccessibleTarget? inputTarget,
        out string unavailableSignal)
    {
        messageList = null!;
        inputTarget = null;
        unavailableSignal = "message-list-unavailable";
        if (targets.MessageLists.Count == 0)
        {
            return false;
        }

        if (requireMatchingUrlInput && targets.InputCandidates.Count != 1)
        {
            unavailableSignal =
                $"url-input-candidate-count:{targets.InputCandidates.Count}";
            return false;
        }

        messageList = targets.MessageLists
            .OrderByDescending(target => GetDirectListItems(target).Count)
            .First();
        inputTarget = requireMatchingUrlInput
            ? targets.InputCandidates[0]
            : null;
        return true;
    }

    private static bool IsCachedMessageListUsable(
        AccessibleTarget messageList) =>
        SafeRole(messageList.Accessible, messageList.ChildId) ==
            RoleSystemList &&
        SafeState(messageList.Accessible, messageList.ChildId) ==
            MessageListState &&
        GetDirectListItems(messageList).Count > 0;

    private static bool TryValidateRequest(
        DiscordConfirmationRequest request,
        out HashSet<string> expectedUrls)
    {
        expectedUrls = [];
        if (!Enum.IsDefined(request.ContentKind) ||
            request.MainWindowHandle == 0 ||
            request.RendererWindowHandle == 0 ||
            request.ProcessId == 0 ||
            request.NormalizedUrls.Count > 20 ||
            (request.ContentKind == DiscordConfirmationContentKind.Url &&
             request.NormalizedUrls.Count == 0) ||
            (request.ContentKind == DiscordConfirmationContentKind.Image &&
             request.NormalizedUrls.Count != 0))
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

        if ((request.ContentKind == DiscordConfirmationContentKind.Url &&
             expectedUrls.Count == 0) ||
            !IsContextValid(request))
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

    internal static bool RequiresMatchingUrlInput(
        DiscordConfirmationRequest request) =>
        request.ContentKind == DiscordConfirmationContentKind.Url &&
        !request.ExplicitSendObserved;

    internal static bool IsCacheContextMatch(
        DiscordConfirmationRequest request,
        long cachedMainWindowHandle,
        long cachedRendererWindowHandle,
        uint cachedProcessId,
        string cachedWindowTitle,
        string currentWindowTitle) =>
        cachedMainWindowHandle == request.MainWindowHandle &&
        cachedRendererWindowHandle == request.RendererWindowHandle &&
        cachedProcessId == request.ProcessId &&
        string.Equals(
            cachedWindowTitle,
            currentWindowTitle,
            StringComparison.Ordinal);

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
        IReadOnlySet<string> expectedUrls,
        bool requireMatchingUrlInput)
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
            requireMatchingUrlInput,
            0);
        return result;
    }

    private static void Traverse(
        AccessibleTarget target,
        IReadOnlySet<string> expectedUrls,
        TargetSearchResult result,
        ISet<long> visited,
        ref int nodeCount,
        bool requireMatchingUrlInput,
        int depth)
    {
        if (IsTargetSearchComplete(
                requireMatchingUrlInput,
                result.MessageLists.Count,
                result.InputCandidates.Count) ||
            depth > MaximumTraversalDepth ||
            nodeCount++ >= MaximumTraversalNodes)
        {
            return;
        }

        var role = SafeRole(target.Accessible, target.ChildId);
        if (requireMatchingUrlInput &&
            role == RoleSystemText &&
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
                requireMatchingUrlInput,
                depth + 1);
            if (IsTargetSearchComplete(
                    requireMatchingUrlInput,
                    result.MessageLists.Count,
                    result.InputCandidates.Count))
            {
                break;
            }
        }
    }

    internal static bool IsTargetSearchComplete(
        bool requireMatchingUrlInput,
        int messageListCount,
        int inputCandidateCount) =>
        messageListCount > 0 &&
        (!requireMatchingUrlInput || inputCandidateCount > 0);

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

    private static bool SubtreeContainsImageAttachment(AccessibleTarget root)
    {
        var visited = new HashSet<long>();
        var nodeCount = 0;
        var hasVisibleGraphic = false;
        var hasImageDescriptor = false;

        void Inspect(AccessibleTarget target, int depth)
        {
            if (depth > MaximumTraversalDepth ||
                nodeCount++ >= MaximumTraversalNodes ||
                (hasVisibleGraphic && hasImageDescriptor))
            {
                return;
            }

            var state = SafeState(target.Accessible, target.ChildId);
            if (SafeRole(target.Accessible, target.ChildId) ==
                    RoleSystemGraphic &&
                (state == 0 || (state & VisibleListItemState) != 0))
            {
                hasVisibleGraphic = true;
            }

            hasImageDescriptor |= LooksLikeImageDescriptor(
                SafeName(target.Accessible, target.ChildId));
            hasImageDescriptor |= LooksLikeImageDescriptor(
                SafeValue(target.Accessible, target.ChildId));
            hasImageDescriptor |= LooksLikeImageDescriptor(
                SafeDescription(target.Accessible, target.ChildId));

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
        return hasVisibleGraphic && hasImageDescriptor;
    }

    private static bool SubtreeContainsOwnedAttachmentControl(
        AccessibleTarget root)
    {
        var visited = new HashSet<long>();
        var nodeCount = 0;
        var found = false;

        void Inspect(AccessibleTarget target, int depth)
        {
            if (found ||
                depth > MaximumTraversalDepth ||
                nodeCount++ >= MaximumTraversalNodes)
            {
                return;
            }

            found = LooksLikeOwnedAttachmentControl(
                        SafeName(target.Accessible, target.ChildId)) ||
                    LooksLikeOwnedAttachmentControl(
                        SafeValue(target.Accessible, target.ChildId)) ||
                    LooksLikeOwnedAttachmentControl(
                        SafeDescription(target.Accessible, target.ChildId));
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
        return found;
    }

    internal static bool LooksLikeImageDescriptor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("image", StringComparison.Ordinal) ||
               normalized.Contains("이미지", StringComparison.Ordinal) ||
               normalized.Contains("attachment", StringComparison.Ordinal) ||
               normalized.Contains("첨부", StringComparison.Ordinal) ||
               normalized.Contains(
                   "cdn.discordapp.com/attachments/",
                   StringComparison.Ordinal) ||
               normalized.Contains(
                   "media.discordapp.net/attachments/",
                   StringComparison.Ordinal) ||
               normalized.EndsWith(".png", StringComparison.Ordinal) ||
               normalized.EndsWith(".jpg", StringComparison.Ordinal) ||
               normalized.EndsWith(".jpeg", StringComparison.Ordinal) ||
               normalized.EndsWith(".gif", StringComparison.Ordinal) ||
               normalized.EndsWith(".webp", StringComparison.Ordinal);
    }

    internal static bool LooksLikeOwnedAttachmentControl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("첨부 파일 수정", StringComparison.Ordinal) ||
               normalized.Contains("edit attachment", StringComparison.Ordinal);
    }

    private static List<AccessibleTarget> GetDirectListItems(
        AccessibleTarget list) =>
        GetChildren(ToAccessible(list))
            .Where(child =>
                SafeRole(child.Accessible, child.ChildId) ==
                RoleSystemListItem)
            .ToList();

    private static List<AccessibleTarget> GetMessagesWithRefresh(
        IAccessible accessibleRoot,
        ref AccessibleTarget messageList)
    {
        var messages = GetDirectListItems(messageList);
        if (messages.Count > 0)
        {
            return messages;
        }

        var refreshed = FindTargets(
                accessibleRoot,
                new HashSet<string>(StringComparer.Ordinal),
                requireMatchingUrlInput: false)
            .MessageLists
            .OrderByDescending(target => GetDirectListItems(target).Count)
            .FirstOrDefault();
        if (refreshed is null)
        {
            return [];
        }

        messageList = refreshed;
        return GetDirectListItems(messageList);
    }

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
        int resultCode;
        int obtained;
        try
        {
            resultCode = AccessibleChildren(
                container,
                0,
                childCount,
                children,
                out obtained);
        }
        catch (COMException)
        {
            return results;
        }
        catch (InvalidCastException)
        {
            return results;
        }

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
        catch (InvalidCastException)
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
        catch (InvalidCastException)
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
        catch (InvalidCastException)
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
        catch (InvalidCastException)
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
        catch (InvalidCastException)
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
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static string? SafeDescription(
        IAccessible accessible,
        object childId)
    {
        try
        {
            return accessible.get_accDescription(childId);
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
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
        catch (InvalidCastException)
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

    private static string GetWindowTitle(nint window)
    {
        var title = new StringBuilder(512);
        return GetWindowText(window, title, title.Capacity) > 0
            ? title.ToString()
            : string.Empty;
    }

    private sealed record AccessibleTarget(
        IAccessible Accessible,
        object ChildId);

    private sealed record TargetResolution(
        IAccessible AccessibleRoot,
        AccessibleTarget MessageList,
        AccessibleTarget? InputTarget,
        bool CacheHit);

    private sealed class WorkerTargetCache
    {
        private long _mainWindowHandle;
        private long _rendererWindowHandle;
        private uint _processId;
        private string _windowTitle = string.Empty;

        public IAccessible? AccessibleRoot { get; private set; }

        public AccessibleTarget? MessageList { get; private set; }

        public AccessibleTarget? InputTarget { get; private set; }

        public bool Matches(
            DiscordConfirmationRequest request,
            string windowTitle) =>
            AccessibleRoot is not null &&
            MessageList is not null &&
            IsCacheContextMatch(
                request,
                _mainWindowHandle,
                _rendererWindowHandle,
                _processId,
                _windowTitle,
                windowTitle);

        public void Update(
            DiscordConfirmationRequest request,
            string windowTitle,
            IAccessible accessibleRoot,
            AccessibleTarget messageList,
            AccessibleTarget? inputTarget)
        {
            var sameContext = IsCacheContextMatch(
                request,
                _mainWindowHandle,
                _rendererWindowHandle,
                _processId,
                    _windowTitle,
                windowTitle);
            _mainWindowHandle = request.MainWindowHandle;
            _rendererWindowHandle = request.RendererWindowHandle;
            _processId = request.ProcessId;
            _windowTitle = windowTitle;
            AccessibleRoot = accessibleRoot;
            MessageList = messageList;
            InputTarget = inputTarget ?? (sameContext ? InputTarget : null);
        }

        public void Clear()
        {
            _mainWindowHandle = 0;
            _rendererWindowHandle = 0;
            _processId = 0;
            _windowTitle = string.Empty;
            AccessibleRoot = null;
            MessageList = null;
            InputTarget = null;
        }
    }

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        nint window,
        StringBuilder text,
        int maximumCount);
}
