namespace Sentory.Platform.Windows.Runtime;

public enum DiscordConfirmationOutcome
{
    Confirmed,
    Cancelled,
    Expired,
    DetectionUnavailable
}

public sealed record DiscordConfirmationRequest(
    long MainWindowHandle,
    long RendererWindowHandle,
    uint ProcessId,
    IReadOnlyList<string> NormalizedUrls,
    int TimeoutMilliseconds = 300_000);

public sealed record DiscordConfirmationResponse(
    DiscordConfirmationOutcome Outcome,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<string> ConfirmationSignals)
{
    public static DiscordConfirmationResponse Unavailable() =>
        new(
            DiscordConfirmationOutcome.DetectionUnavailable,
            null,
            []);
}

public interface IDiscordConfirmationClient
{
    Task<DiscordConfirmationResponse> ConfirmAsync(
        DiscordConfirmationRequest request,
        CancellationToken cancellationToken = default);
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
    int DirectMessageCount,
    bool LatestNewMessageContainsExpectedUrls);

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
            observation.DirectMessageCount > baselineDirectMessageCount &&
            observation.LatestNewMessageContainsExpectedUrls)
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
