using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal readonly record struct LineRecentSendSignal(
    DateTimeOffset SentAt,
    string? ComposerText,
    bool ImageDialogSendObserved = false);

internal sealed class LineRecentSendSignals
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, LineRecentSendSignal> _signals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<uint, LineRecentSendSignal> _processSignals = [];

    public void Observe(string contextHash, DateTimeOffset sentAt) =>
        Observe(contextHash, sentAt, composerText: null);

    public void Observe(
        string contextHash,
        DateTimeOffset sentAt,
        string? composerText,
        bool imageDialogSendObserved = false) =>
        _signals[contextHash] = new LineRecentSendSignal(
            sentAt,
            composerText,
            imageDialogSendObserved);

    public void ObserveProcess(uint processId, DateTimeOffset sentAt) =>
        ObserveProcess(processId, sentAt, composerText: null);

    public void ObserveProcess(
        uint processId,
        DateTimeOffset sentAt,
        string? composerText,
        bool imageDialogSendObserved = false) =>
        _processSignals[processId] = new LineRecentSendSignal(
            sentAt,
            composerText,
            imageDialogSendObserved);

    public bool CanApply(
        string contextHash,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt)
    {
        RemoveExpired(observedAt);
        return _signals.TryGetValue(contextHash, out var signal) &&
               IsApplicable(signal, pastedAt, observedAt);
    }

    public bool CanApply(
        string contextHash,
        uint processId,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt) =>
        CanApply(contextHash, pastedAt, observedAt) ||
        CanApplyProcess(processId, pastedAt, observedAt);

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

    public bool TryGetApplicable(
        string contextHash,
        uint processId,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt,
        IReadOnlyList<NormalizedUrl> urls,
        bool hasImages,
        out LineRecentSendSignal signal)
    {
        RemoveExpired(observedAt);
        if (_signals.TryGetValue(contextHash, out var exact) &&
            IsApplicable(exact, pastedAt, observedAt) &&
            HasMatchingContent(exact, urls, hasImages))
        {
            signal = exact;
            return true;
        }

        if (_processSignals.TryGetValue(processId, out var process) &&
            IsApplicable(process, pastedAt, observedAt) &&
            HasMatchingContent(process, urls, hasImages))
        {
            signal = process;
            return true;
        }

        signal = default;
        return false;
    }

    private bool CanApplyProcess(
        uint processId,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt)
    {
        RemoveExpired(observedAt);
        return _processSignals.TryGetValue(processId, out var signal) &&
               IsApplicable(signal, pastedAt, observedAt);
    }

    private static bool IsApplicable(
        LineRecentSendSignal signal,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt) =>
        signal.SentAt >= pastedAt &&
        observedAt >= signal.SentAt &&
        observedAt - signal.SentAt <= Retention;

    private static bool HasMatchingContent(
        LineRecentSendSignal signal,
        IReadOnlyList<NormalizedUrl> urls,
        bool hasImages) =>
        hasImages ||
        LineMessageMatchPolicy.HasMatchingComposerEvidence(
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
