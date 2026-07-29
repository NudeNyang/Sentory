using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class MessengerSendCandidatePolicyTests
{
    [Fact]
    public void OneSendInputSelectsOnlyLatestEligibleCandidate()
    {
        var now = DateTimeOffset.Parse("2026-07-29T03:00:00Z");
        Candidate[] candidates =
        [
            new("first", now, true),
            new("newest-ineligible", now.AddSeconds(2), false),
            new("latest-eligible", now.AddSeconds(1), true)
        ];

        var selected = MessengerSendCandidatePolicy.SelectLatestEligible(
            candidates,
            candidate => candidate.Eligible,
            candidate => candidate.OccurredAt);

        Assert.Equal("latest-eligible", selected?.Name);
    }

    private sealed record Candidate(
        string Name,
        DateTimeOffset OccurredAt,
        bool Eligible);
}
