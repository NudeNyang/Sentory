namespace Sentory.Platform.Windows.Runtime;

public enum DiscordConfirmationOutcome
{
    Confirmed,
    Cancelled,
    Expired,
    DetectionUnavailable
}

public enum DiscordConfirmationContentKind
{
    Url,
    Image,
    AttachmentDiscovery,
    Warmup,
    DraftImageInspection
}

public enum DiscordWorkerOperation
{
    Confirm,
    Cancel
}

public sealed record DiscordWorkerMessage(
    Guid RequestId,
    DiscordWorkerOperation Operation,
    DiscordConfirmationRequest? Request);

public sealed record DiscordWorkerResponse(
    Guid RequestId,
    DiscordConfirmationResponse Response);

public sealed record DiscordConfirmationRequest(
    long MainWindowHandle,
    long RendererWindowHandle,
    uint ProcessId,
    DiscordConfirmationContentKind ContentKind,
    IReadOnlyList<string> NormalizedUrls,
    int TimeoutMilliseconds = 300_000,
    bool ExplicitSendObserved = false,
    int? ExpectedDraftImageCount = null);

public sealed record DiscordConfirmationResponse(
    DiscordConfirmationOutcome Outcome,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<string> ConfirmationSignals,
    IReadOnlyList<string>? AttachmentUrls = null,
    int? DraftImageCount = null,
    IReadOnlyList<string>? ConfirmedUrls = null)
{
    public static DiscordConfirmationResponse Unavailable(
        params string[] signals) =>
        new(
            DiscordConfirmationOutcome.DetectionUnavailable,
            null,
            signals);
}

public interface IDiscordConfirmationClient
{
    Task<DiscordConfirmationResponse> ConfirmAsync(
        DiscordConfirmationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IDiscordWorkerLifecycle
{
    event EventHandler? RecoveryRequired;
}

internal enum DiscordCandidateDecision
{
    Pending,
    Confirmed,
    Cancelled
}

internal readonly record struct DiscordCandidateObservation(
    bool ContextValid,
    bool InputContainsExpectedUrls,
    bool InputIsEmpty,
    int NewMessageCount,
    int DirectMessageCount,
    bool MatchingNewMessageFound);

internal readonly record struct DiscordImageCandidateObservation(
    bool ContextValid,
    int NewMessageCount,
    int DirectMessageCount,
    bool MatchingNewOwnedImageFound);

internal static class DiscordConfirmationEvaluator
{
    public static DiscordCandidateDecision Evaluate(
        int baselineDirectMessageCount,
        DiscordCandidateObservation observation)
    {
        if (!observation.ContextValid)
        {
            return DiscordCandidateDecision.Cancelled;
        }

        if (observation.InputIsEmpty &&
            observation.MatchingNewMessageFound)
        {
            return DiscordCandidateDecision.Confirmed;
        }

        if (!observation.InputContainsExpectedUrls &&
            !observation.InputIsEmpty)
        {
            return DiscordCandidateDecision.Cancelled;
        }

        return DiscordCandidateDecision.Pending;
    }
}

internal static class DiscordImageConfirmationEvaluator
{
    public static DiscordCandidateDecision Evaluate(
        int baselineDirectMessageCount,
        DiscordImageCandidateObservation observation)
    {
        if (!observation.ContextValid)
        {
            return DiscordCandidateDecision.Cancelled;
        }

        if (observation.MatchingNewOwnedImageFound)
        {
            return DiscordCandidateDecision.Confirmed;
        }

        return DiscordCandidateDecision.Pending;
    }
}

internal static class DiscordManualUploadConfirmationPolicy
{
    public static bool CanConfirm(
        bool trackDraft,
        bool observedDraft,
        bool matchingOwnedImageFound) =>
        matchingOwnedImageFound && (!trackDraft || observedDraft);

    public static bool ShouldCancel(
        bool trackDraft,
        bool observedDraft,
        int draftImageCount,
        DateTimeOffset? draftMissingSince,
        DateTimeOffset now) =>
        trackDraft &&
        observedDraft &&
        draftImageCount == 0 &&
        draftMissingSince is not null &&
        now - draftMissingSince >= TimeSpan.FromSeconds(2);
}
