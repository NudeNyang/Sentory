using System.Windows;
using System.Windows.Automation;
using Sentory.Core;

namespace Sentory.Platform.Windows.Interop;

internal sealed record SlackAccessibilitySnapshot(
    string ConversationIdentity,
    string? CurrentUserName,
    IReadOnlySet<string> MessageIds);

internal sealed record SlackConfirmationRequest(
    ValidatedSlackContext Context,
    SlackAccessibilitySnapshot Baseline,
    IReadOnlyList<NormalizedUrl> Urls,
    IReadOnlyList<string> ImageFileNames,
    bool HasImages,
    TimeSpan Timeout);

internal sealed record SlackConfirmationResponse(
    bool Confirmed,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<string> Signals);

internal sealed record SlackAccessibleMessage(
    string Id,
    string Text,
    bool HasMeaningfulImage);

internal sealed class SlackDraftConfirmationState(
    TimeSpan cancellationGracePeriod)
{
    private DateTimeOffset? _missingSince;

    public bool DraftObserved { get; private set; }

    public bool ShouldCancel(
        bool matchingDraftPresent,
        bool explicitSendObserved,
        DateTimeOffset observedAt)
    {
        if (matchingDraftPresent)
        {
            DraftObserved = true;
            _missingSince = null;
            return false;
        }

        if (!DraftObserved || explicitSendObserved)
        {
            return false;
        }

        _missingSince ??= observedAt;
        return observedAt - _missingSince >= cancellationGracePeriod;
    }
}

internal interface ISlackAccessibilityClient
{
    Task<SlackAccessibilitySnapshot?> TryCaptureAsync(
        ValidatedSlackContext context,
        bool requireFocusedComposer,
        CancellationToken cancellationToken);

    Task<SlackConfirmationResponse> WaitForConfirmationAsync(
        SlackConfirmationRequest request,
        Func<bool> explicitSendObserved,
        CancellationToken cancellationToken);
}

internal static class SlackMessageMatchPolicy
{
    private static readonly string[] ImageLabels =
    [
        "image",
        "photo",
        "이미지",
        "사진",
        "画像",
        "写真",
        "图片",
        "照片"
    ];

    private static readonly string[] AttachmentRemoveLabels =
    [
        "remove file",
        "remove attachment",
        "파일 제거",
        "첨부 파일 제거",
        "ファイルを削除",
        "添付ファイルを削除",
        "移除文件",
        "移除附件",
        "刪除檔案",
        "移除附件"
    ];

    public static string? ParseCurrentUserName(string? profileButtonName)
    {
        if (string.IsNullOrWhiteSpace(profileButtonName))
        {
            return null;
        }

        var separator = profileButtonName.IndexOfAny([':', '：']);
        if (separator < 0 || separator == profileButtonName.Length - 1)
        {
            return null;
        }

        var prefix = profileButtonName[..separator].Trim();
        if (!prefix.Equals("user", StringComparison.OrdinalIgnoreCase) &&
            !prefix.Equals("사용자", StringComparison.Ordinal) &&
            !prefix.Equals("ユーザー", StringComparison.Ordinal) &&
            !prefix.Equals("用户", StringComparison.Ordinal) &&
            !prefix.Equals("使用者", StringComparison.Ordinal))
        {
            return null;
        }

        var value = profileButtonName[(separator + 1)..].Trim();
        return value.Length == 0 ? null : value;
    }

    public static bool IsOwnMessage(
        string messageText,
        string? currentUserName,
        bool explicitSendObserved)
    {
        if (string.IsNullOrWhiteSpace(currentUserName))
        {
            return explicitSendObserved;
        }

        return Normalize(messageText).StartsWith(
            Normalize(currentUserName),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsEveryUrl(
        string messageText,
        IReadOnlyList<NormalizedUrl> urls)
    {
        if (urls.Count == 0)
        {
            return true;
        }

        var normalizedText = Normalize(messageText);
        return urls.All(url =>
        {
            var value = Normalize(url.Value).TrimEnd('/');
            if (normalizedText.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Uri.TryCreate(url.Value, UriKind.Absolute, out var uri) &&
                   normalizedText.Contains(
                       Normalize(uri.Host + uri.PathAndQuery).TrimEnd('/'),
                       StringComparison.OrdinalIgnoreCase);
        });
    }

    public static bool ContainsImage(
        string messageText,
        IReadOnlyList<string> fileNames,
        bool hasMeaningfulImageElement)
    {
        if (hasMeaningfulImageElement)
        {
            return true;
        }

        var normalizedText = Normalize(messageText);
        if (fileNames.Any(fileName =>
                !string.IsNullOrWhiteSpace(fileName) &&
                normalizedText.Contains(
                    Normalize(fileName),
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ImageLabels.Any(label =>
            normalizedText.Contains(label, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAttachmentRemoveLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Normalize(value);
        return AttachmentRemoveLabels.Any(label =>
            normalized.Contains(label, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) =>
        string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
}

internal static class SlackConversationMatchPolicy
{
    public static bool IsSameConversation(
        SlackAccessibilitySnapshot baseline,
        string currentIdentity,
        IReadOnlyCollection<SlackAccessibleMessage> currentMessages)
    {
        if (baseline.MessageIds.Count == 0)
        {
            return string.Equals(
                currentIdentity,
                baseline.ConversationIdentity,
                StringComparison.Ordinal);
        }

        var requiredOverlap = Math.Min(3, baseline.MessageIds.Count);
        var overlap = currentMessages.Count(message =>
            baseline.MessageIds.Contains(message.Id));
        return overlap >= requiredOverlap;
    }
}

internal sealed class SlackAccessibilityClient(
    Action<string, string>? diagnostic = null) : ISlackAccessibilityClient
{
    private static readonly PropertyCondition ListCondition = new(
        AutomationElement.ControlTypeProperty,
        ControlType.List);
    private static readonly PropertyCondition ListItemCondition = new(
        AutomationElement.ControlTypeProperty,
        ControlType.ListItem);
    private static readonly PropertyCondition ButtonCondition = new(
        AutomationElement.ControlTypeProperty,
        ControlType.Button);

    public Task<SlackAccessibilitySnapshot?> TryCaptureAsync(
        ValidatedSlackContext context,
        bool requireFocusedComposer,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => TryCapture(
                context,
                requireFocusedComposer,
                cancellationToken),
            cancellationToken);

    public async Task<SlackConfirmationResponse> WaitForConfirmationAsync(
        SlackConfirmationRequest request,
        Func<bool> explicitSendObserved,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var draftState = new SlackDraftConfirmationState(
            TimeSpan.FromSeconds(10));
        bool? lastDraftPresence = null;
        string? lastConversationIdentity = null;
        while (DateTimeOffset.UtcNow - startedAt < request.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var root = AutomationElement.FromHandle(
                    request.Context.MainWindow);
                var messageList = FindMessageList(root);
                var conversationIdentity = messageList is null
                    ? null
                    : CreateConversationIdentity(messageList);
                var currentMessages = messageList is null
                    ? []
                    : ReadMessages(messageList);
                var sameConversation = messageList is not null &&
                    SlackConversationMatchPolicy.IsSameConversation(
                        request.Baseline,
                        conversationIdentity!,
                        currentMessages);
                if (!string.Equals(
                        $"{conversationIdentity}|{sameConversation}",
                        lastConversationIdentity,
                        StringComparison.Ordinal))
                {
                    lastConversationIdentity =
                        $"{conversationIdentity}|{sameConversation}";
                    diagnostic?.Invoke(
                        "slack-conversation-state",
                        $"available={conversationIdentity is not null} stable={sameConversation}");
                }

                if (messageList is not null && sameConversation)
                {
                    var newMessages = currentMessages
                        .Where(message =>
                            !request.Baseline.MessageIds.Contains(message.Id))
                        .ToList();
                    foreach (var message in newMessages)
                    {
                        var sendObserved = explicitSendObserved();
                        if (!SlackMessageMatchPolicy.IsOwnMessage(
                                message.Text,
                                request.Baseline.CurrentUserName,
                                sendObserved) ||
                            !SlackMessageMatchPolicy.ContainsEveryUrl(
                                message.Text,
                                request.Urls) ||
                            (request.HasImages &&
                             !SlackMessageMatchPolicy.ContainsImage(
                                 message.Text,
                                 request.ImageFileNames,
                                 message.HasMeaningfulImage)))
                        {
                            continue;
                        }

                        var signals = new List<string>
                        {
                            "slack-accessibility",
                            "slack-conversation-stable",
                            "slack-new-message-id",
                            "slack-own-message"
                        };
                        signals.Add(request.HasImages
                            ? "slack-image-message"
                            : "slack-url-match");
                        if (sendObserved)
                        {
                            signals.Add("slack-send-key");
                        }

                        diagnostic?.Invoke(
                            "slack-send-confirmed",
                            $"urls={request.Urls.Count} images={(request.HasImages ? 1 : 0)} sendKey={sendObserved}");
                        return new SlackConfirmationResponse(
                            true,
                            DateTimeOffset.UtcNow,
                            signals);
                    }

                    var matchingDraftPresent = IsMatchingDraftPresent(
                        root,
                        request.Urls,
                        request.ImageFileNames,
                        request.HasImages);
                    if (matchingDraftPresent != lastDraftPresence)
                    {
                        lastDraftPresence = matchingDraftPresent;
                        diagnostic?.Invoke(
                            "slack-draft-state",
                            $"present={matchingDraftPresent} urls={request.Urls.Count} images={(request.HasImages ? 1 : 0)}");
                    }

                    var draftSendObserved = explicitSendObserved();
                    if (draftState.ShouldCancel(
                            matchingDraftPresent,
                            draftSendObserved,
                            DateTimeOffset.UtcNow))
                    {
                        diagnostic?.Invoke(
                            "slack-candidate-cancelled",
                            $"reason=draft-removed urls={request.Urls.Count} images={(request.HasImages ? 1 : 0)}");
                        return new SlackConfirmationResponse(
                            false,
                            null,
                            ["slack-draft-cancelled"]);
                    }
                }
            }
            catch (Exception exception)
                when (exception is ElementNotAvailableException or
                      InvalidOperationException or
                      ArgumentException)
            {
                diagnostic?.Invoke(
                    "slack-accessibility-retry",
                    $"type={exception.GetType().Name}");
            }

            await Task.Delay(250, cancellationToken);
        }

        diagnostic?.Invoke(
            "slack-candidate-expired",
            $"urls={request.Urls.Count} images={(request.HasImages ? 1 : 0)} draftObserved={draftState.DraftObserved}");
        return new SlackConfirmationResponse(
            false,
            null,
            ["slack-confirmation-timeout"]);
    }

    private SlackAccessibilitySnapshot? TryCapture(
        ValidatedSlackContext context,
        bool requireFocusedComposer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = AutomationElement.FromHandle(context.MainWindow);
            if (requireFocusedComposer &&
                !IsFocusedComposer(root, context.ProcessId))
            {
                diagnostic?.Invoke(
                    "slack-context-rejected",
                    "reason=focused-element-not-composer");
                return null;
            }

            var messageList = FindMessageList(root);
            if (messageList is null)
            {
                diagnostic?.Invoke(
                    "slack-context-rejected",
                    "reason=message-list-unavailable");
                return null;
            }

            var messages = ReadMessages(messageList);
            var snapshot = new SlackAccessibilitySnapshot(
                CreateConversationIdentity(messageList),
                FindCurrentUserName(root),
                messages.Select(message => message.Id)
                    .ToHashSet(StringComparer.Ordinal));
            diagnostic?.Invoke(
                "slack-context-ready",
                $"messages={snapshot.MessageIds.Count} userKnown={snapshot.CurrentUserName is not null}");
            return snapshot;
        }
        catch (Exception exception)
            when (exception is ElementNotAvailableException or
                  InvalidOperationException or
                  ArgumentException)
        {
            diagnostic?.Invoke(
                "slack-context-failed",
                $"type={exception.GetType().Name}");
            return null;
        }
    }

    private static bool IsFocusedComposer(
        AutomationElement root,
        uint processId)
    {
        var focused = AutomationElement.FocusedElement;
        if (focused is null ||
            SafeProcessId(focused) != checked((int)processId) ||
            !Equals(SafeControlType(focused), ControlType.Edit) ||
            SafeIsOffscreen(focused))
        {
            return false;
        }

        var rootBounds = SafeBounds(root);
        var focusedBounds = SafeBounds(focused);
        return rootBounds.Height > 0 &&
               focusedBounds.Height > 0 &&
               focusedBounds.Top >= rootBounds.Top + rootBounds.Height * 0.45 &&
               focusedBounds.Left >= rootBounds.Left &&
               focusedBounds.Right <= rootBounds.Right;
    }

    private static bool IsMatchingDraftPresent(
        AutomationElement root,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<string> imageFileNames,
        bool hasImages)
    {
        var composerEdit = FindComposerEdit(root);
        if (composerEdit is null)
        {
            return false;
        }

        var composerText = new List<string>();
        AddElementText(composerEdit, composerText);
        foreach (AutomationElement descendant in composerEdit.FindAll(
                     TreeScope.Descendants,
                     System.Windows.Automation.Condition.TrueCondition))
        {
            AddElementText(descendant, composerText);
        }

        if (!SlackMessageMatchPolicy.ContainsEveryUrl(
                string.Join(' ', composerText),
                urls))
        {
            return false;
        }

        if (!hasImages)
        {
            return true;
        }

        var composer = FindComposerContainer(root, composerEdit);
        if (composer is null)
        {
            return false;
        }

        var attachmentText = new List<string>();
        var hasMeaningfulImage = false;
        var hasAttachmentRemoveButton = false;
        foreach (AutomationElement descendant in composer.FindAll(
                     TreeScope.Descendants,
                     System.Windows.Automation.Condition.TrueCondition))
        {
            AddElementText(descendant, attachmentText);
            if (Equals(SafeControlType(descendant), ControlType.Image) &&
                !string.IsNullOrWhiteSpace(SafeName(descendant)))
            {
                hasMeaningfulImage = true;
            }

            if (Equals(SafeControlType(descendant), ControlType.Button) &&
                SlackMessageMatchPolicy.IsAttachmentRemoveLabel(
                    SafeName(descendant)))
            {
                hasAttachmentRemoveButton = true;
            }
        }

        return SlackMessageMatchPolicy.ContainsImage(
            string.Join(' ', attachmentText),
            imageFileNames,
            hasMeaningfulImage && hasAttachmentRemoveButton) ||
               hasAttachmentRemoveButton;
    }

    private static AutomationElement? FindComposerEdit(AutomationElement root)
    {
        var rootBounds = SafeBounds(root);
        AutomationElement? best = null;
        var bestTop = double.MinValue;
        var editCondition = new PropertyCondition(
            AutomationElement.ControlTypeProperty,
            ControlType.Edit);
        foreach (AutomationElement candidate in
                 root.FindAll(TreeScope.Descendants, editCondition))
        {
            var bounds = SafeBounds(candidate);
            if (SafeIsOffscreen(candidate) ||
                rootBounds.Height <= 0 ||
                bounds.Height <= 0 ||
                bounds.Top < rootBounds.Top + rootBounds.Height * 0.45 ||
                bounds.Left < rootBounds.Left ||
                bounds.Right > rootBounds.Right ||
                bounds.Top <= bestTop)
            {
                continue;
            }

            best = candidate;
            bestTop = bounds.Top;
        }

        return best;
    }

    private static AutomationElement? FindComposerContainer(
        AutomationElement root,
        AutomationElement composerEdit)
    {
        var rootBounds = SafeBounds(root);
        var walker = TreeWalker.ControlViewWalker;
        var current = composerEdit;
        AutomationElement? best = null;
        for (var depth = 0; depth < 8; depth++)
        {
            var parent = walker.GetParent(current);
            if (parent is null || Equals(parent, root))
            {
                break;
            }

            var bounds = SafeBounds(parent);
            if (rootBounds.Height <= 0 ||
                bounds.Height <= 0 ||
                bounds.Top < rootBounds.Top + rootBounds.Height * 0.45)
            {
                break;
            }

            best = parent;
            current = parent;
        }

        return best;
    }

    private static AutomationElement? FindMessageList(AutomationElement root)
    {
        AutomationElement? best = null;
        var bestScore = 0;
        foreach (AutomationElement candidate in
                 root.FindAll(TreeScope.Descendants, ListCondition))
        {
            var messages = ReadMessages(candidate);
            var score = messages.Count;
            if (score <= bestScore)
            {
                continue;
            }

            best = candidate;
            bestScore = score;
        }

        return best;
    }

    private static IReadOnlyList<SlackAccessibleMessage> ReadMessages(
        AutomationElement messageList)
    {
        var messages = new List<SlackAccessibleMessage>();
        foreach (AutomationElement item in
                 messageList.FindAll(TreeScope.Children, ListItemCondition))
        {
            var id = SafeAutomationId(item);
            if (string.IsNullOrWhiteSpace(id) ||
                !id.StartsWith("message-list_", StringComparison.Ordinal))
            {
                continue;
            }

            var textParts = new List<string>();
            AddElementText(item, textParts);
            var hasMeaningfulImage = false;
            foreach (AutomationElement descendant in
                     item.FindAll(
                         TreeScope.Descendants,
                         System.Windows.Automation.Condition.TrueCondition))
            {
                AddElementText(descendant, textParts);
                if (Equals(SafeControlType(descendant), ControlType.Image) &&
                    !string.IsNullOrWhiteSpace(SafeName(descendant)))
                {
                    hasMeaningfulImage = true;
                }
            }

            messages.Add(new SlackAccessibleMessage(
                id,
                string.Join(' ', textParts.Distinct(StringComparer.Ordinal)),
                hasMeaningfulImage));
        }

        return messages;
    }

    private static string? FindCurrentUserName(AutomationElement root)
    {
        foreach (AutomationElement button in
                 root.FindAll(TreeScope.Descendants, ButtonCondition))
        {
            var parsed = SlackMessageMatchPolicy.ParseCurrentUserName(
                SafeName(button));
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static string CreateConversationIdentity(
        AutomationElement messageList) =>
        $"{SafeAutomationId(messageList)}|{SafeName(messageList)}";

    private static void AddElementText(
        AutomationElement element,
        ICollection<string> values)
    {
        AddIfPresent(values, SafeName(element));
        AddIfPresent(values, SafeValue(element));
        AddIfPresent(values, SafeHelpText(element));
    }

    private static void AddIfPresent(
        ICollection<string> values,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static string SafeName(AutomationElement element) =>
        SafeRead(() => element.Current.Name) ?? string.Empty;

    private static string SafeAutomationId(AutomationElement element) =>
        SafeRead(() => element.Current.AutomationId) ?? string.Empty;

    private static string SafeHelpText(AutomationElement element) =>
        SafeRead(() => element.Current.HelpText) ?? string.Empty;

    private static string SafeValue(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(
                       ValuePattern.Pattern,
                       out var pattern) &&
                   pattern is ValuePattern valuePattern
                ? valuePattern.Current.Value ?? string.Empty
                : string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static ControlType? SafeControlType(AutomationElement element) =>
        SafeRead(() => element.Current.ControlType);

    private static int SafeProcessId(AutomationElement element) =>
        SafeRead(() => element.Current.ProcessId);

    private static bool SafeIsOffscreen(AutomationElement element) =>
        SafeRead(() => element.Current.IsOffscreen, true);

    private static Rect SafeBounds(AutomationElement element) =>
        SafeRead(() => element.Current.BoundingRectangle, Rect.Empty);

    private static T? SafeRead<T>(Func<T> read, T? fallback = default)
    {
        try
        {
            return read();
        }
        catch (ElementNotAvailableException)
        {
            return fallback;
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

}
