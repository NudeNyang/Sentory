namespace Sentory.Platform.Windows.Runtime;

internal static class MessengerSendCandidatePolicy
{
    public static T? SelectLatestEligible<T>(
        IEnumerable<T> candidates,
        Func<T, bool> isEligible,
        Func<T, DateTimeOffset> occurredAt)
        where T : class =>
        candidates
            .Where(isEligible)
            .OrderByDescending(occurredAt)
            .FirstOrDefault();
}
