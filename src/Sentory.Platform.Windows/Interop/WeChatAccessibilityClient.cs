using System.Windows.Automation;
using Sentory.Core;

namespace Sentory.Platform.Windows.Interop;

internal sealed record WeChatAccessibilitySnapshot(
    string ConversationIdentity,
    IReadOnlySet<string> MessageIds);

internal sealed record WeChatConfirmationRequest(
    ValidatedWeChatContext Context,
    WeChatAccessibilitySnapshot Baseline,
    IReadOnlyList<NormalizedUrl> Urls,
    bool HasImages,
    TimeSpan Timeout);

internal sealed record WeChatConfirmationResponse(
    bool Confirmed,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<string> Signals);

internal sealed record WeChatAccessibleMessage(string Id, string Text);

internal interface IWeChatAccessibilityClient
{
    Task<WeChatAccessibilitySnapshot?> TryCaptureAsync(
        ValidatedWeChatContext context,
        bool requireFocusedComposer,
        CancellationToken cancellationToken);

    Task<WeChatConfirmationResponse> WaitForConfirmationAsync(
        WeChatConfirmationRequest request,
        Func<bool> explicitSendObserved,
        CancellationToken cancellationToken);
}

internal interface IWeChatPointerSendVerifier
{
    bool IsPotentialSendControl(
        int screenX,
        int screenY,
        uint processId,
        nint mainWindow);
}

internal static class WeChatMessageMatchPolicy
{
    public static bool HasMatchingSendEvidence(
        string messageText,
        IReadOnlyList<NormalizedUrl> urls) =>
        urls.Count == 0 ||
        ContainsEveryUrl(messageText, urls);

    public static bool ContainsEveryUrl(
        string messageText,
        IReadOnlyList<NormalizedUrl> urls)
    {
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

internal static class WeChatNewMessageConfirmationPolicy
{
    public static bool IsConfirmed(
        string messageText,
        IReadOnlyList<NormalizedUrl> urls,
        bool explicitSendObserved) =>
        explicitSendObserved &&
        WeChatMessageMatchPolicy.HasMatchingSendEvidence(
            messageText,
            urls);
}

internal static class WeChatConversationMatchPolicy
{
    public static bool IsSameConversation(
        WeChatAccessibilitySnapshot baseline,
        string currentIdentity,
        IReadOnlyCollection<WeChatAccessibleMessage> currentMessages)
    {
        if (!string.Equals(
                baseline.ConversationIdentity,
                currentIdentity,
                StringComparison.Ordinal))
        {
            return false;
        }

        return baseline.MessageIds.Count == 0 ||
               currentMessages.Any(message =>
                   baseline.MessageIds.Contains(message.Id));
    }
}

internal sealed class WeChatPointerSendVerifier : IWeChatPointerSendVerifier
{
    private static readonly string[] SendLabels =
        ["Send", "发送", "보내기", "送信"];

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
                if (WeChatAutomation.SafeProcessId(current) !=
                    checked((int)processId))
                {
                    return false;
                }

                if (string.Equals(
                        WeChatAutomation.SafeClassName(current),
                        WeChatAutomation.SendViewClassName,
                        StringComparison.Ordinal) ||
                    (WeChatAutomation.SafeControlType(current) ==
                         ControlType.Button &&
                     SendLabels.Contains(
                         WeChatAutomation.SafeName(current).Trim(),
                         StringComparer.OrdinalIgnoreCase)))
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
            when (WeChatAutomation.IsRecoverable(exception))
        {
        }

        return false;
    }
}

internal sealed class WeChatAccessibilityClient(
    INativeWindowApi native,
    Action<string, string>? diagnostic = null) : IWeChatAccessibilityClient
{
    public Task<WeChatAccessibilitySnapshot?> TryCaptureAsync(
        ValidatedWeChatContext context,
        bool requireFocusedComposer,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => TryCapture(context, requireFocusedComposer, cancellationToken),
            cancellationToken);

    public async Task<WeChatConfirmationResponse> WaitForConfirmationAsync(
        WeChatConfirmationRequest request,
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
                var messageList = WeChatAutomation.FindByAutomationId(
                    root,
                    WeChatAutomation.MessageListAutomationId);
                if (messageList is not null &&
                    TryReadConversationIdentity(root, out var identity))
                {
                    var messages = ReadMessages(messageList);
                    if (!WeChatConversationMatchPolicy.IsSameConversation(
                            request.Baseline,
                            identity,
                            messages))
                    {
                        diagnostic?.Invoke(
                            "wechat-candidate-cancelled",
                            "reason=conversation-changed");
                        return new WeChatConfirmationResponse(
                            false,
                            null,
                            ["wechat-conversation-changed"]);
                    }

                    foreach (var message in messages.Where(message =>
                                 !request.Baseline.MessageIds.Contains(
                                     message.Id)))
                    {
                        if (!WeChatNewMessageConfirmationPolicy.IsConfirmed(
                                message.Text,
                                request.Urls,
                                explicitSendObserved()))
                        {
                            continue;
                        }

                        diagnostic?.Invoke(
                            "wechat-send-confirmed",
                            $"urls={request.Urls.Count} images={(request.HasImages ? 1 : 0)}");
                        return new WeChatConfirmationResponse(
                            true,
                            DateTimeOffset.UtcNow,
                            [
                                "wechat-accessibility",
                                "wechat-conversation-stable",
                                "wechat-new-message-id",
                                "wechat-explicit-send-input",
                                request.HasImages
                                    ? "wechat-image-message"
                                    : "wechat-url-match"
                            ]);
                    }
                }
            }
            catch (Exception exception)
                when (WeChatAutomation.IsRecoverable(exception))
            {
                diagnostic?.Invoke(
                    "wechat-accessibility-retry",
                    $"type={exception.GetType().Name}");
            }

            await Task.Delay(200, cancellationToken);
        }

        diagnostic?.Invoke(
            "wechat-candidate-expired",
            $"urls={request.Urls.Count} images={(request.HasImages ? 1 : 0)}");
        return new WeChatConfirmationResponse(
            false,
            null,
            ["wechat-confirmation-timeout"]);
    }

    private WeChatAccessibilitySnapshot? TryCapture(
        ValidatedWeChatContext context,
        bool requireFocusedComposer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = AutomationElement.FromHandle(context.MainWindow);
            var composer = WeChatAutomation.FindByAutomationId(
                root,
                WeChatAutomation.ComposerAutomationId);
            var messageList = WeChatAutomation.FindByAutomationId(
                root,
                WeChatAutomation.MessageListAutomationId);
            if (WeChatAutomation.SafeProcessId(root) !=
                    checked((int)context.ProcessId) ||
                composer is null ||
                messageList is null ||
                (requireFocusedComposer &&
                 !WeChatAutomation.IsFocusedElement(composer) &&
                 !IsOwnedDialogFocused(context)))
            {
                diagnostic?.Invoke(
                    "wechat-context-rejected",
                    composer is null
                        ? "reason=message-composer-unavailable"
                        : messageList is null
                            ? "reason=message-list-unavailable"
                            : "reason=focused-element-not-composer");
                return null;
            }

            if (!TryReadConversationIdentity(root, out var identity))
            {
                diagnostic?.Invoke(
                    "wechat-context-rejected",
                    "reason=selected-conversation-unavailable");
                return null;
            }

            var messages = ReadMessages(messageList);
            var snapshot = new WeChatAccessibilitySnapshot(
                identity,
                messages.Select(message => message.Id)
                    .ToHashSet(StringComparer.Ordinal));
            diagnostic?.Invoke(
                "wechat-context-ready",
                $"messages={snapshot.MessageIds.Count}");
            return snapshot;
        }
        catch (Exception exception)
            when (WeChatAutomation.IsRecoverable(exception))
        {
            diagnostic?.Invoke(
                "wechat-context-failed",
                $"type={exception.GetType().Name}");
            return null;
        }
    }

    private static IReadOnlyList<WeChatAccessibleMessage> ReadMessages(
        AutomationElement messageList)
    {
        var messages = new List<WeChatAccessibleMessage>();
        foreach (AutomationElement item in messageList.FindAll(
                     TreeScope.Descendants,
                     new PropertyCondition(
                         AutomationElement.ControlTypeProperty,
                         ControlType.ListItem)))
        {
            var id = WeChatAutomation.CreateRuntimeIdentity(item);
            if (id.Length == 0)
            {
                continue;
            }

            var text = string.Join(
                ' ',
                WeChatAutomation.ReadTextValues(item)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal));
            messages.Add(new WeChatAccessibleMessage(id, text));
        }

        return messages;
    }

    private bool IsOwnedDialogFocused(ValidatedWeChatContext context)
    {
        var focused = AutomationElement.FocusedElement;
        if (focused is null ||
            WeChatAutomation.SafeProcessId(focused) !=
            checked((int)context.ProcessId))
        {
            return false;
        }

        var current = native.GetRootWindow(native.GetForegroundWindow());
        if (current == nint.Zero || current == context.MainWindow ||
            native.GetProcessId(current) != context.ProcessId)
        {
            return false;
        }

        for (var depth = 0; depth < 5; depth++)
        {
            var owner = native.GetOwnerWindow(current);
            if (owner == nint.Zero)
            {
                return false;
            }

            var ownerRoot = native.GetRootWindow(owner);
            if (ownerRoot == context.MainWindow)
            {
                return true;
            }

            if (ownerRoot == nint.Zero || ownerRoot == current ||
                native.GetProcessId(ownerRoot) != context.ProcessId)
            {
                return false;
            }

            current = ownerRoot;
        }

        return false;
    }

    private static bool TryReadConversationIdentity(
        AutomationElement root,
        out string identity)
    {
        identity = string.Empty;
        var sessionList = WeChatAutomation.FindByAutomationId(
            root,
            WeChatAutomation.SessionListAutomationId);
        if (sessionList is null ||
            !sessionList.TryGetCurrentPattern(
                SelectionPattern.Pattern,
                out var selectionObject) ||
            selectionObject is not SelectionPattern selection)
        {
            return false;
        }

        var selected = selection.Current.GetSelection();
        if (selected.Length != 1)
        {
            return false;
        }

        identity = WeChatAutomation.CreateElementIdentity(selected[0]);
        return identity.Length > 0;
    }
}

internal static class WeChatAutomation
{
    public const string ComposerAutomationId = "chat_input_field";
    public const string MessageListAutomationId = "chat_message_list";
    public const string SessionListAutomationId = "session_list";
    public const string SendViewClassName = "mmui::ChatInputSendView";

    public static AutomationElement? FindByAutomationId(
        AutomationElement root,
        string automationId) =>
        root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                automationId));

    public static bool IsFocusedElement(AutomationElement expected)
    {
        var focused = AutomationElement.FocusedElement;
        return focused is not null &&
               string.Equals(
                   CreateElementIdentity(focused),
                   CreateElementIdentity(expected),
                   StringComparison.Ordinal);
    }

    public static string CreateElementIdentity(AutomationElement element)
    {
        var automationId = SafeRead(() => element.Current.AutomationId) ??
                           string.Empty;
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            return $"automation:{automationId}";
        }

        return CreateRuntimeIdentity(element);
    }

    public static string CreateRuntimeIdentity(AutomationElement element)
    {
        try
        {
            return $"runtime:{string.Join('.', element.GetRuntimeId())}";
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return string.Empty;
        }
    }

    public static IReadOnlyList<string> ReadTextValues(
        AutomationElement element)
    {
        var values = new List<string>();
        AddTextValues(element, values);
        foreach (AutomationElement descendant in element.FindAll(
                     TreeScope.Descendants,
                     Condition.TrueCondition))
        {
            AddTextValues(descendant, values);
        }

        return values;
    }

    public static string SafeClassName(AutomationElement element) =>
        SafeRead(() => element.Current.ClassName) ?? string.Empty;

    public static string SafeName(AutomationElement element) =>
        SafeRead(() => element.Current.Name) ?? string.Empty;

    public static ControlType SafeControlType(AutomationElement element) =>
        SafeRead(() => element.Current.ControlType, ControlType.Custom) ??
        ControlType.Custom;

    public static int SafeProcessId(AutomationElement element) =>
        SafeRead(() => element.Current.ProcessId);

    public static bool IsRecoverable(Exception exception) =>
        exception is ElementNotAvailableException or
            InvalidOperationException or
            ArgumentException or
            OverflowException;

    private static void AddTextValues(
        AutomationElement element,
        ICollection<string> values)
    {
        var name = SafeRead(() => element.Current.Name) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(name))
        {
            values.Add(name);
        }

        AddSemanticTextValues(element, values);
    }

    private static void AddSemanticTextValues(
        AutomationElement element,
        ICollection<string> values)
    {
        try
        {
            if (element.TryGetCurrentPattern(
                    ValuePattern.Pattern,
                    out var valueObject) &&
                valueObject is ValuePattern valuePattern)
            {
                values.Add(valuePattern.Current.Value ?? string.Empty);
            }

            if (element.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out var textObject) &&
                textObject is TextPattern textPattern)
            {
                values.Add(textPattern.DocumentRange.GetText(-1) ??
                           string.Empty);
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
        }
    }

    private static T? SafeRead<T>(Func<T> read, T? fallback = default)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return fallback;
        }
    }
}
