using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal readonly record struct WeChatRecentSendSignal(
    DateTimeOffset SentAt,
    string? ComposerText);

internal sealed class WeChatRecentSendSignals
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, WeChatRecentSendSignal> _signals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<uint, WeChatRecentSendSignal> _processSignals = [];

    public void Observe(
        string contextHash,
        DateTimeOffset sentAt,
        string? composerText) =>
        _signals[contextHash] = new WeChatRecentSendSignal(
            sentAt,
            composerText);

    public void ObserveProcess(
        uint processId,
        DateTimeOffset sentAt,
        string? composerText) =>
        _processSignals[processId] = new WeChatRecentSendSignal(
            sentAt,
            composerText);

    public bool CanApply(
        string contextHash,
        uint processId,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt,
        IReadOnlyList<NormalizedUrl> urls,
        bool hasImages)
    {
        RemoveExpired(observedAt);
        if (_signals.TryGetValue(contextHash, out var exact) &&
            IsApplicable(exact, pastedAt, observedAt) &&
            HasMatchingContent(exact, urls, hasImages))
        {
            return true;
        }

        return _processSignals.TryGetValue(processId, out var process) &&
               IsApplicable(process, pastedAt, observedAt) &&
               HasMatchingContent(process, urls, hasImages);
    }

    private static bool IsApplicable(
        WeChatRecentSendSignal signal,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt) =>
        signal.SentAt >= pastedAt &&
        observedAt >= signal.SentAt &&
        observedAt - signal.SentAt <= Retention;

    private static bool HasMatchingContent(
        WeChatRecentSendSignal signal,
        IReadOnlyList<NormalizedUrl> urls,
        bool hasImages) =>
        hasImages ||
        WeChatMessageMatchPolicy.HasMatchingComposerEvidence(
            signal.ComposerText ?? string.Empty,
            urls);

    private void RemoveExpired(DateTimeOffset observedAt)
    {
        foreach (var contextHash in _signals
                     .Where(pair =>
                         observedAt >= pair.Value.SentAt &&
                         observedAt - pair.Value.SentAt > Retention)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _signals.Remove(contextHash);
        }

        foreach (var processId in _processSignals
                     .Where(pair =>
                         observedAt >= pair.Value.SentAt &&
                         observedAt - pair.Value.SentAt > Retention)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _processSignals.Remove(processId);
        }
    }
}
