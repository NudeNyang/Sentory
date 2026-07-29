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
            AutomationElement? imageDialog = null;
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

                if (string.Equals(
                        current.Current.ClassName,
                        "AlertWindow",
                        StringComparison.Ordinal))
                {
                    imageDialog = current;
                    break;
                }

                if (Equals(current, root))
                {
                    break;
                }

                current = walker.GetParent(current);
            }

            if (imageDialog is not null)
            {
                var bounds = imageDialog.Current.BoundingRectangle;
                return LineImageDialogSendButtonPolicy.IsWithin(
                    new WindowBounds(
                        checked((int)bounds.Left),
                        checked((int)bounds.Top),
                        checked((int)bounds.Right),
                        checked((int)bounds.Bottom)),
                    screenX,
                    screenY);
            }
        }
        catch (Exception exception)
            when (exception is ElementNotAvailableException or
                  InvalidOperationException or
                  ArgumentException or
                  OverflowException)
        {
        }

        return false;
    }
}

internal static class LineImageDialogSendButtonPolicy
{
    public static bool IsWithin(
        WindowBounds bounds,
        int screenX,
        int screenY) =>
        bounds.Width >= 240 &&
        bounds.Height >= 180 &&
        screenX >= bounds.Left + bounds.Width * 0.5 &&
        screenX < bounds.Right &&
        screenY >= bounds.Top + bounds.Height * 0.65 &&
        screenY < bounds.Bottom;
}

internal sealed record LineAccessibilitySnapshot(
    string ConversationIdentity,
    IReadOnlySet<string> MessageIds,
    bool ImageSendDialogFocused = false,
    bool IsUnanchored = false);

internal sealed record LineConfirmationRequest(
    ValidatedLineContext Context,
    LineAccessibilitySnapshot Baseline,
    IReadOnlyList<NormalizedUrl> Urls,
    bool HasImages,
    TimeSpan Timeout);

internal sealed record LineConfirmationResponse(
    bool Confirmed,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<string> Signals,
    LineAccessibilitySnapshot? ObservedSnapshot = null);

internal sealed record LineAccessibleMessage(
    string Id,
    string Text);

internal interface ILineAccessibilityClient
{
    Task<LineAccessibilitySnapshot?> TryCaptureAsync(
        ValidatedLineContext context,
        bool requireFocusedComposer,
        bool allowImageSendDialog,
        CancellationToken cancellationToken,
        bool reportDiagnostics = true);

    Task<LineConfirmationResponse> WaitForConfirmationAsync(
        LineConfirmationRequest request,
        Func<bool> explicitSendObserved,
        Func<bool> imageDialogSendObserved,
        CancellationToken cancellationToken);
}

internal static class LineMessageMatchPolicy
{
    public static bool HasMatchingSendEvidence(
        string messageText,
        IReadOnlyList<NormalizedUrl> urls,
        bool preSendComposerMatched) =>
        preSendComposerMatched &&
        (urls.Count == 0 ||
         (string.IsNullOrWhiteSpace(messageText) ||
          ContainsEveryUrl(messageText, urls)));

    public static bool HasMatchingComposerEvidence(
        string composerText,
        IReadOnlyList<NormalizedUrl> urls) =>
        urls.Count == 0 ||
        (!string.IsNullOrWhiteSpace(composerText) &&
         ContainsEveryUrl(composerText, urls));

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

internal readonly record struct LineComposerTextSnapshot(
    bool IsAvailable,
    string Text);

internal interface ILineComposerTextReader
{
    LineComposerTextSnapshot Read(
        nint mainWindow,
        uint processId);
}

internal sealed class LineComposerTextReader : ILineComposerTextReader
{
    private static readonly PropertyCondition ComposerCondition = new(
        AutomationElement.ClassNameProperty,
        "AutoSuggestTextArea");

    public LineComposerTextSnapshot Read(
        nint mainWindow,
        uint processId)
    {
        try
        {
            var root = AutomationElement.FromHandle(mainWindow);
            var composer = root.FindFirst(
                TreeScope.Descendants,
                ComposerCondition);
            if (composer is null ||
                composer.Current.ProcessId != checked((int)processId))
            {
                return new LineComposerTextSnapshot(false, string.Empty);
            }

            var available = false;
            var values = new List<string>();
            if (composer.TryGetCurrentPattern(
                    ValuePattern.Pattern,
                    out var valueObject) &&
                valueObject is ValuePattern valuePattern)
            {
                available = true;
                values.Add(valuePattern.Current.Value ?? string.Empty);
            }

            if (composer.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out var textObject) &&
                textObject is TextPattern textPattern)
            {
                available = true;
                values.Add(textPattern.DocumentRange.GetText(-1) ??
                           string.Empty);
            }

            values.Add(composer.Current.Name ?? string.Empty);
            return new LineComposerTextSnapshot(
                available,
                values.FirstOrDefault(value =>
                    !string.IsNullOrWhiteSpace(value)) ?? string.Empty);
        }
        catch (Exception exception)
            when (exception is ElementNotAvailableException or
                  InvalidOperationException or
                  ArgumentException or
                  OverflowException)
        {
            return new LineComposerTextSnapshot(false, string.Empty);
        }
    }
}

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
    public static bool CanCreateBaseline(
        bool identityAvailable,
        int messageCount) =>
        identityAvailable ||
        messageCount > 0;

    public static bool IsSameConversation(
        LineAccessibilitySnapshot baseline,
        string currentIdentity,
        IReadOnlyCollection<LineAccessibleMessage> currentMessages)
    {
        if (!string.IsNullOrEmpty(baseline.ConversationIdentity) &&
            !string.Equals(
                baseline.ConversationIdentity,
                currentIdentity,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (baseline.MessageIds.Count == 0)
        {
            return !string.IsNullOrEmpty(baseline.ConversationIdentity);
        }

        var requiredOverlap = 1;
        return currentMessages.Count(message =>
                   baseline.MessageIds.Contains(message.Id)) >=
               requiredOverlap;
    }

    public static bool HasConversationChanged(
        LineAccessibilitySnapshot baseline,
        string currentIdentity) =>
        !string.IsNullOrEmpty(baseline.ConversationIdentity) &&
        !string.IsNullOrEmpty(currentIdentity) &&
        !string.Equals(
            baseline.ConversationIdentity,
            currentIdentity,
            StringComparison.Ordinal);
}

internal static class LineImageConfirmationPolicy
{
    public static bool CanConfirm(
        LineAccessibilitySnapshot baseline,
        string currentIdentity,
        IReadOnlyCollection<LineAccessibleMessage> currentMessages,
        bool explicitSendObserved,
        bool imageDialogSendObserved)
    {
        if (!explicitSendObserved ||
            !imageDialogSendObserved ||
            currentMessages.Count == 0 ||
            LineConversationMatchPolicy.HasConversationChanged(
                baseline,
                currentIdentity))
        {
            return false;
        }

        return baseline.IsUnanchored ||
               currentMessages.Any(message =>
                   !baseline.MessageIds.Contains(message.Id));
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
        CancellationToken cancellationToken,
        bool reportDiagnostics = true) =>
        Task.Run(
            () => TryCapture(
                context,
                requireFocusedComposer,
                allowImageSendDialog,
                cancellationToken,
                reportDiagnostics),
            cancellationToken);

    public async Task<LineConfirmationResponse> WaitForConfirmationAsync(
        LineConfirmationRequest request,
        Func<bool> explicitSendObserved,
        Func<bool> imageDialogSendObserved,
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
                if (list is not null)
                {
                    var messages = ReadMessages(list);
                    var identity = conversationPanel is not null &&
                                   TryCreateConversationIdentity(
                                       conversationPanel,
                                       out var selectedIdentity)
                        ? selectedIdentity
                        : string.Empty;
                    var observedSnapshot = new LineAccessibilitySnapshot(
                        identity,
                        messages.Select(message => message.Id)
                            .ToHashSet(StringComparer.Ordinal));
                    var sameConversation =
                        LineConversationMatchPolicy.IsSameConversation(
                            request.Baseline,
                            identity,
                            messages);
                    if (sameConversation)
                    {
                        foreach (var message in messages.Where(message =>
                                     !request.Baseline.MessageIds.Contains(
                                         message.Id)))
                        {
                            var preSendComposerMatched =
                                explicitSendObserved();
                            if (!preSendComposerMatched ||
                                !LineMessageMatchPolicy.HasMatchingSendEvidence(
                                    message.Text,
                                    request.Urls,
                                    preSendComposerMatched))
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
                                ],
                                observedSnapshot);
                        }
                    }
                    else if (request.HasImages &&
                             LineImageConfirmationPolicy.CanConfirm(
                                 request.Baseline,
                                 identity,
                                 messages,
                                 explicitSendObserved(),
                                 imageDialogSendObserved()))
                    {
                        diagnostic?.Invoke(
                            "line-send-confirmed",
                            $"urls=0 images=1 mode=image-dialog-message-change");
                        return new LineConfirmationResponse(
                            true,
                            DateTimeOffset.UtcNow,
                            [
                                "line-accessibility",
                                "line-message-list-changed",
                                "line-explicit-send-input",
                                "line-image-dialog-send"
                            ],
                            observedSnapshot);
                    }
                    else if (LineConversationMatchPolicy.HasConversationChanged(
                                 request.Baseline,
                                 identity))
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
        CancellationToken cancellationToken,
        bool reportDiagnostics)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = AutomationElement.FromHandle(context.MainWindow);
            var conversationPanel = FindConversationPanel(root);
            var messageView = FindMessageView(root);
            var composer = FindComposer(root);
            var focus = requireFocusedComposer || allowImageSendDialog
                ? CaptureComposerFocus(root, composer)
                : null;
            if (messageView is null ||
                composer is null ||
                (requireFocusedComposer &&
                 focus is { IsUsable: false } &&
                 (!allowImageSendDialog ||
                  !focus.IsImageSendDialogUsable)))
            {
                if (reportDiagnostics)
                {
                    diagnostic?.Invoke(
                        "line-context-rejected",
                        messageView is null
                            ? "reason=message-view-unavailable"
                            : composer is null
                                ? "reason=message-composer-unavailable"
                                : CreateFocusDiagnostic(focus!));
                }

                return null;
            }

            var list = FindMessageList(messageView);
            if (list is null)
            {
                if (reportDiagnostics)
                {
                    diagnostic?.Invoke(
                        "line-context-rejected",
                        "reason=message-list-unavailable");
                }

                return null;
            }

            var messages = ReadMessages(list);
            var selectedIdentity = string.Empty;
            var identityAvailable = conversationPanel is not null &&
                                    TryCreateConversationIdentity(
                                        conversationPanel,
                                        out selectedIdentity);
            if (!LineConversationMatchPolicy.CanCreateBaseline(
                    identityAvailable,
                    messages.Count))
            {
                if (reportDiagnostics)
                {
                    diagnostic?.Invoke(
                        "line-context-rejected",
                        conversationPanel is null
                            ? $"reason=conversation-panel-unavailable messages={messages.Count} imageDialog={focus?.IsImageSendDialogUsable == true}"
                            : "reason=selected-conversation-unavailable");
                }

                return null;
            }

            var snapshot = new LineAccessibilitySnapshot(
                identityAvailable ? selectedIdentity : string.Empty,
                messages.Select(message => message.Id)
                    .ToHashSet(StringComparer.Ordinal),
                focus?.IsImageSendDialogUsable == true);
            if (reportDiagnostics)
            {
                diagnostic?.Invoke(
                    "line-context-ready",
                    $"messages={snapshot.MessageIds.Count} identity={identityAvailable} imageDialog={snapshot.ImageSendDialogFocused}");
            }

            return snapshot;
        }
        catch (Exception exception)
            when (exception is ElementNotAvailableException or
                  InvalidOperationException or
                  ArgumentException)
        {
            if (reportDiagnostics)
            {
                diagnostic?.Invoke(
                    "line-context-failed",
                    $"type={exception.GetType().Name}");
            }

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
