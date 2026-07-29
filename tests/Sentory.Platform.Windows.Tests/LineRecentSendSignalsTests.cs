using Sentory.Core;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class LineRecentSendSignalsTests
{
    [Fact]
    public void AppliesEnterObservedBeforeCandidateRegistration()
    {
        var signals = new LineRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T06:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(40);

        signals.Observe("same-window", sentAt);

        Assert.True(signals.CanApply(
            "same-window",
            pastedAt,
            sentAt.AddMilliseconds(500)));
    }

    [Fact]
    public void RejectsSendFromAnotherWindowOrBeforePaste()
    {
        var signals = new LineRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T06:00:00Z");

        signals.Observe("other-window", pastedAt.AddMilliseconds(40));
        signals.Observe("same-window", pastedAt.AddMilliseconds(-40));

        Assert.False(signals.CanApply(
            "same-window",
            pastedAt,
            pastedAt.AddMilliseconds(500)));
        Assert.False(signals.CanApply(
            "missing-window",
            pastedAt,
            pastedAt.AddMilliseconds(500)));
    }

    [Fact]
    public void RejectsExpiredSendSignal()
    {
        var signals = new LineRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T06:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(40);

        signals.Observe("same-window", sentAt);

        Assert.False(signals.CanApply(
            "same-window",
            pastedAt,
            sentAt.AddMinutes(3)));
    }

    [Fact]
    public void ReplaysFastSendAfterSlowNativeDropBaselineCapture()
    {
        var signals = new LineRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        signals.Observe(
            "same-window",
            droppedAt.AddMilliseconds(80));

        Assert.True(signals.CanApply(
            "same-window",
            droppedAt,
            droppedAt.AddMilliseconds(1500)));
    }

    [Fact]
    public void ReplaysImageDialogEvidenceAfterCandidateRegistrationDelay()
    {
        var signals = new LineRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        signals.Observe(
            "same-window",
            droppedAt.AddMilliseconds(80),
            composerText: null,
            imageDialogSendObserved: true);

        Assert.True(signals.TryTakeApplicable(
            "same-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(1500),
            [],
            hasImages: true,
            out var signal));
        Assert.True(signal.ImageDialogSendObserved);
        Assert.False(signals.TryTakeApplicable(
            "same-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(1501),
            [],
            hasImages: true,
            out _));
    }

    [Fact]
    public void ReplaysSameProcessSendWhenPhotoDialogHasNoOwner()
    {
        var signals = new LineRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        signals.ObserveProcess(42, droppedAt.AddMilliseconds(80));

        Assert.True(signals.CanApply(
            "main-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(500)));
        Assert.False(signals.CanApply(
            "main-window",
            99,
            droppedAt,
            droppedAt.AddMilliseconds(500)));
    }

    [Fact]
    public void ReplaysUrlSendOnlyWhenComposerStillContainedCandidate()
    {
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/path",
            out var url));
        var droppedAt = DateTimeOffset.UtcNow;

        var matching = new LineRecentSendSignals();
        matching.Observe(
            "same-window",
            droppedAt.AddMilliseconds(80),
            "https://example.com/path");
        Assert.True(matching.CanApply(
            "same-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(500),
            [url],
            hasImages: false));

        var deleted = new LineRecentSendSignals();
        deleted.Observe(
            "same-window",
            droppedAt.AddMilliseconds(80),
            "다른 메시지");
        Assert.False(deleted.CanApply(
            "same-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(500),
            [url],
            hasImages: false));
    }

    [Fact]
    public void ImmediateCandidateConsumptionPreventsRegistrationReplay()
    {
        var signals = new LineRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        var sentAt = droppedAt.AddMilliseconds(80);
        signals.Observe(
            "same-window",
            sentAt,
            composerText: null,
            imageDialogSendObserved: true);
        signals.ObserveProcess(
            42,
            sentAt,
            composerText: null,
            imageDialogSendObserved: true);

        Assert.True(signals.TryConsume(sentAt));

        Assert.False(signals.TryTakeApplicable(
            "same-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(1500),
            [],
            hasImages: true,
            out _));
    }

    [Fact]
    public void TakingContextSignalAlsoConsumesSameProcessAlias()
    {
        var signals = new LineRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        var sentAt = droppedAt.AddMilliseconds(80);
        signals.Observe("same-window", sentAt);
        signals.ObserveProcess(42, sentAt);

        Assert.True(signals.TryTakeApplicable(
            "same-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(500),
            [],
            hasImages: true,
            out _));

        Assert.False(signals.TryTakeApplicable(
            "other-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(501),
            [],
            hasImages: true,
            out _));
    }
}
