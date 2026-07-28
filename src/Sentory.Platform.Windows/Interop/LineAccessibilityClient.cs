using System.Windows.Automation;
using Sentory.Core;

namespace Sentory.Platform.Windows.Interop;

internal interface ILinePointerSendVerifier
{
    bool IsPotentialSendControl(
        int screenX,
        int screenY,
        uint processId,
        nint mainWindow);
}

internal sealed class LinePointerSendVerifier : ILinePointerSendVerifier
{
    public bool IsPotentialSendControl(
        int screenX,
        int screenY,
        uint processId,
        nint mainWindow)
    {
        try
        {
            var root = AutomationElement.FromHandle(mainWindow);
            var current = AutomationElement.FromPoint(
                new System.Windows.Point(screenX, screenY));
            var walker = TreeWalker.RawViewWalker;
            for (var depth = 0;
                 current is not null && depth < 12;
                 depth++)
            {
                if (current.Current.ProcessId != checked((int)processId))
                {
                    return false;
                }

                if (string.Equals(
                        current.Current.ClassName,
                        "LcButton",
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (Equals(current, root))
                {
                    break;
                }

                current = walker.GetParent(current);
            }
        }
        catch (Exception exception)
            when (exception is ElementNotAvailableException or
                  InvalidOperationException or
                  ArgumentException)
        {
        }

        return false;
    }
}

internal sealed record LineAccessibilitySnapshot(
    string ConversationIdentity,
    IReadOnlySet<string> MessageIds,
    bool ImageSendDialogFocused = false);

internal sealed record LineConfirmationRequest(
    ValidatedLineContext Context,
    LineAccessibilitySnapshot Baseline,
    IReadOnlyList<NormalizedUrl> Urls,
    bool HasImages,
    TimeSpan Timeout);

internal sealed record LineConfirmationResponse(
    bool Confirmed,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<string> Signals);

internal sealed record LineAccessibleMessage(
    string Id,
    string Text);

internal interface ILineAccessibilityClient
{
    Task<LineAccessibilitySnapshot?> TryCaptureAsync(
        ValidatedLineContext context,
        bool requireFocusedComposer,
        bool allowImageSendDialog,
        CancellationToken cancellationToken);

    Task<LineConfirmationResponse> WaitForConfirmationAsync(
        LineConfirmationRequest request,
        Func<bool> explicitSendObserved,
        CancellationToken cancellationToken);
}

internal static class LineMessageMatchPolicy
{
    public static bool HasMatchingSendEvidence(
        string messageText,
        IReadOnlyList<NormalizedUrl> urls) =>
        urls.Count == 0 ||
        string.IsNullOrWhiteSpace(messageText) ||
        ContainsEveryUrl(messageText, urls);

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
            if (normalizedText.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Uri.TryCreate(url.Value, UriKind.Absolute, out var uri) &&
                   normalizedText.Contains(
                       Normalize(uri.Host + uri.PathAndQuery).TrimEnd('/'),
                       StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string Normalize(string value) =>
        string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
}

internal static class LineComposerFocusPolicy
{
    public static bool IsUsable(
        bool composerVisible,
        bool focusedMatchesComposer,
        bool sameProcess) =>
        composerVisible &&
        focusedMatchesComposer &&
        sameProcess;

    public static bool IsImageSendDialogUsable(
        bool composerVisible,
        string focusedClassName,
        bool sameProcess) =>
        composerVisible &&
        sameProcess &&
        string.Equals(
            focusedClassName,
            "AlertWindow",
            StringComparison.Ordinal);
}

internal sealed record LineComposerFocusSnapshot(
    bool ComposerVisible,
    bool FocusedPresent,
    string FocusedClassName,
    string FocusedControlKind,
    bool FocusedMatchesComposer,
    bool SameProcess,
    bool IsUsable,
    bool IsImageSendDialogUsable);

internal static class LineConversationIdentityPolicy
{
    public static bool TryCreate(
        IReadOnlyCollection<string> selectedIds,
        out string identity)
    {
        identity = string.Empty;
        if (selectedIds.Count != 1)
        {
            return false;
        }

        identity = selectedIds.Single();
        return !string.IsNullOrWhiteSpace(identity);
    }
}

internal static class LineConversationMatchPolicy
{
    public static bool IsSameConversation(
        LineAccessibilitySnapshot baseline,
        string currentIdentity,
        IReadOnlyCollection<LineAccessibleMessage> currentMessages)
    {
        if (!string.Equals(
                baseline.ConversationIdentity,
                currentIdentity,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (baseline.MessageIds.Count == 0)
        {
            return true;
        }

        var requiredOverlap = 1;
        return currentMessages.Count(message =>
                   baseline.MessageIds.Contains(message.Id)) >=
               requiredOverlap;
    }
}

internal sealed class LineAccessibilityClient(
    Action<string, string>? diagnostic = null) : ILineAccessibilityClient
{
    private static readonly PropertyCondition ConversationPanelCondition = new(
        AutomationElement.ClassNameProperty,
        "MainChatPanel");
    private static readonly PropertyCondition MessageViewCondition = new(
        AutomationElement.ClassNameProperty,
        "ChatMessageView");
    private static readonly PropertyCondition MessageListCondition = new(
        AutomationElement.ClassNameProperty,
        "LcListView");
    private static readonly PropertyCondition ListItemCondition = new(
        AutomationElement.ControlTypeProperty,
        ControlType.ListItem);
    private static readonly PropertyCondition ComposerCondition = new(
        AutomationElement.ClassNameProperty,
        "AutoSuggestTextArea");

    public Task<LineAccessibilitySnapshot?> TryCaptureAsync(
        ValidatedLineContext context,
        bool requireFocusedComposer,
        bool allowImageSendDialog,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => TryCapture(
                context,
                requireFocusedComposer,
                allowImageSendDialog,
                cancellationToken),
            cancellationToken);

    public async Task<LineConfirmationResponse> WaitForConfirmationAsync(
        LineConfirmationRequest request,
        Func<bool> explicitSendObserved,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < request.Timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var root = AutomationElement.FromHandle(
                    request.Context.MainWindow);
                var conversationPanel = FindConversationPanel(root);
                var messageView = FindMessageView(root);
                var list = messageView is null
                    ? null
                    : FindMessageList(messageView);
                if (conversationPanel is not null &&
                    list is not null &&
                    TryCreateConversationIdentity(
                        conversationPanel,
                        out var identity))
                {
                    var messages = ReadMessages(list);
                    if (LineConversationMatchPolicy.IsSameConversation(
                            request.Baseline,
                            identity,
                            messages))
                    {
                        foreach (var message in messages.Where(message =>
                                     !request.Baseline.MessageIds.Contains(
                                         message.Id)))
                        {
                            if (!explicitSendObserved() ||
                                !LineMessageMatchPolicy.HasMatchingSendEvidence(
                                    message.Text,
                                    request.Urls))
                            {
                                continue;
                            }

                            diagnostic?.Invoke(
                                "line-send-confirmed",
                                $"urls={request.Urls.Count} images={(request.HasImages ? 1 : 0)}");
                            return new LineConfirmationResponse(
                                true,
                                DateTimeOffset.UtcNow,
                                [
                                    "line-accessibility",
                                    "line-conversation-stable",
                                    "line-new-message-id",
                                    "line-explicit-send-input",
                                    request.HasImages
                                        ? "line-image-message"
                                        : "line-url-match"
                                ]);
                        }
                    }
                    else
                    {
                        diagnostic?.Invoke(
                            "line-candidate-cancelled",
                            "reason=conversation-changed");
                        return new LineConfirmationResponse(
                            false,
                            null,
                            ["line-conversation-changed"]);
                    }
                }
            }
            catch (Exception exception)
                when (exception is ElementNotAvailableException or
                      InvalidOperationException or
                      ArgumentException)
            {
                diagnostic?.Invoke(
                    "line-accessibility-retry",
                    $"type={exception.GetType().Name}");
            }

            await Task.Delay(200, cancellationToken);
        }

        diagnostic?.Invoke(
            "line-candidate-expired",
            $"urls={request.Urls.Count} images={(request.HasImages ? 1 : 0)}");
        return new LineConfirmationResponse(
            false,
            null,
            ["line-confirmation-timeout"]);
    }

    private LineAccessibilitySnapshot? TryCapture(
        ValidatedLineContext context,
        bool requireFocusedComposer,
        bool allowImageSendDialog,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = AutomationElement.FromHandle(context.MainWindow);
            var conversationPanel = FindConversationPanel(root);
            var messageView = FindMessageView(root);
            var composer = FindComposer(root);
            var focus = requireFocusedComposer
                ? CaptureComposerFocus(root, composer)
                : null;
            if (conversationPanel is null ||
                messageView is null ||
                composer is null ||
                (focus is { IsUsable: false } &&
                 (!allowImageSendDialog ||
                  !focus.IsImageSendDialogUsable)))
            {
                diagnostic?.Invoke(
                    "line-context-rejected",
                    conversationPanel is null
                        ? "reason=conversation-panel-unavailable"
                        : messageView is null
                            ? "reason=message-view-unavailable"
                            : composer is null
                                ? "reason=message-composer-unavailable"
                                : CreateFocusDiagnostic(focus!));
                return null;
            }

            var list = FindMessageList(messageView);
            if (list is null)
            {
                diagnostic?.Invoke(
                    "line-context-rejected",
                    "reason=message-list-unavailable");
                return null;
            }

            if (!TryCreateConversationIdentity(
                    conversationPanel,
                    out var conversationIdentity))
            {
                diagnostic?.Invoke(
                    "line-context-rejected",
                    "reason=selected-conversation-unavailable");
                return null;
            }

            var messages = ReadMessages(list);
            var snapshot = new LineAccessibilitySnapshot(
                conversationIdentity,
                messages.Select(message => message.Id)
                    .ToHashSet(StringComparer.Ordinal),
                focus?.IsImageSendDialogUsable == true);
            diagnostic?.Invoke(
                "line-context-ready",
                $"messages={snapshot.MessageIds.Count}");
            return snapshot;
        }
        catch (Exception exception)
            when (exception is ElementNotAvailableException or
                  InvalidOperationException or
                  ArgumentException)
        {
            diagnostic?.Invoke(
                "line-context-failed",
                $"type={exception.GetType().Name}");
            return null;
        }
    }

    private static LineComposerFocusSnapshot CaptureComposerFocus(
        AutomationElement root,
        AutomationElement? composer)
    {
        var composerVisible = composer is not null &&
                              !SafeIsOffscreen(composer);
        var focused = AutomationElement.FocusedElement;
        var focusedPresent = focused is not null;
        var focusedClassName = focusedPresent
            ? SafeClassName(focused!)
            : string.Empty;
        var focusedMatchesComposer = focusedPresent &&
                                     composer is not null &&
                                     string.Equals(
                                         SafeRuntimeId(focused!),
                                         SafeRuntimeId(composer),
                                         StringComparison.Ordinal);
        var sameProcess = focusedPresent &&
                          SafeProcessId(focused!) == SafeProcessId(root);
        return new LineComposerFocusSnapshot(
            composerVisible,
            focusedPresent,
            focusedClassName,
            focusedPresent
                ? SafeControlKind(focused!)
                : "None",
            focusedMatchesComposer,
            sameProcess,
            LineComposerFocusPolicy.IsUsable(
                composerVisible,
                focusedMatchesComposer,
                sameProcess),
            LineComposerFocusPolicy.IsImageSendDialogUsable(
                composerVisible,
                focusedClassName,
                sameProcess));
    }

    private static string CreateFocusDiagnostic(
        LineComposerFocusSnapshot focus) =>
        "reason=focused-element-not-composer " +
        $"composerVisible={focus.ComposerVisible} " +
        $"focusedPresent={focus.FocusedPresent} " +
        $"focusedClass={SanitizeDiagnosticToken(focus.FocusedClassName)} " +
        $"focusedType={focus.FocusedControlKind} " +
        $"matchesComposer={focus.FocusedMatchesComposer} " +
        $"sameProcess={focus.SameProcess}";

    private static AutomationElement? FindConversationPanel(
        AutomationElement root) =>
        root.FindFirst(TreeScope.Descendants, ConversationPanelCondition);

    private static AutomationElement? FindMessageView(
        AutomationElement root) =>
        root.FindFirst(TreeScope.Descendants, MessageViewCondition);

    private static AutomationElement? FindComposer(
        AutomationElement root) =>
        root.FindFirst(TreeScope.Descendants, ComposerCondition);

    private static AutomationElement? FindMessageList(
        AutomationElement panel) =>
        panel.FindFirst(TreeScope.Descendants, MessageListCondition);

    private static IReadOnlyList<LineAccessibleMessage> ReadMessages(
        AutomationElement messageList)
    {
        var messages = new List<LineAccessibleMessage>();
        foreach (AutomationElement item in messageList.FindAll(
                     TreeScope.Children,
                     ListItemCondition))
        {
            var runtimeId = SafeRuntimeId(item);
            if (runtimeId.Length == 0)
            {
                continue;
            }

            var text = string.Join(
                ' ',
                new[] { SafeName(item), SafeValue(item) }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal));
            messages.Add(new LineAccessibleMessage(runtimeId, text));
        }

        return messages;
    }

    private static bool TryCreateConversationIdentity(
        AutomationElement conversationPanel,
        out string identity)
    {
        identity = string.Empty;
        var conversationList = conversationPanel.FindFirst(
            TreeScope.Descendants,
            MessageListCondition);
        if (conversationList is null)
        {
            return false;
        }

        var selectedIds = new List<string>();
        foreach (AutomationElement item in conversationList.FindAll(
                     TreeScope.Children,
                     ListItemCondition))
        {
            if (!SafeIsSelected(item))
            {
                continue;
            }

            var runtimeId = SafeRuntimeId(item);
            if (runtimeId.Length > 0)
            {
                selectedIds.Add(runtimeId);
            }
        }

        return LineConversationIdentityPolicy.TryCreate(
            selectedIds,
            out identity);
    }

    private static string SafeRuntimeId(AutomationElement element)
    {
        try
        {
            return string.Join('.', element.GetRuntimeId());
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

    private static string SafeName(AutomationElement element) =>
        SafeRead(() => element.Current.Name) ?? string.Empty;

    private static string SafeClassName(AutomationElement element) =>
        SafeRead(() => element.Current.ClassName) ?? string.Empty;

    private static int SafeProcessId(AutomationElement element) =>
        SafeRead(() => element.Current.ProcessId);

    private static bool SafeIsOffscreen(AutomationElement element) =>
        SafeRead(() => element.Current.IsOffscreen, true);

    private static string SafeControlKind(AutomationElement element)
    {
        var controlType = SafeRead(
            () => element.Current.ControlType,
            ControlType.Custom);
        if (controlType == ControlType.Edit)
        {
            return "Edit";
        }

        if (controlType == ControlType.Button)
        {
            return "Button";
        }

        return controlType == ControlType.List
            ? "List"
            : "Other";
    }

    private static string SanitizeDiagnosticToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "empty";
        }

        return new string(
            value.Take(64)
                .Select(character => char.IsLetterOrDigit(character) ||
                                     character is '_' or '-'
                    ? character
                    : '_')
                .ToArray());
    }

    private static bool SafeIsSelected(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(
                       SelectionItemPattern.Pattern,
                       out var pattern) &&
                   pattern is SelectionItemPattern selection &&
                   selection.Current.IsSelected;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

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
