namespace Sentory.Platform.Windows.Ocr;

internal sealed record OcrRecognitionCandidate(
    string Text,
    IReadOnlyList<string> Lines,
    string Language,
    double Confidence,
    IReadOnlyList<string>? PreferredTitleLines = null);

internal static class MultilingualOcrResultSelector
{
    private const double MinimumConfidence = 0.45;

    public static OcrRecognitionCandidate Select(
        IReadOnlyList<OcrRecognitionCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return Empty();
        }

        var selected = candidates
            .Select(candidate => (Candidate: candidate, Score: Score(candidate)))
            .OrderByDescending(value => value.Score)
            .ThenByDescending(value => value.Candidate.Confidence)
            .First()
            .Candidate;
        return selected.Confidence >= MinimumConfidence &&
               selected.Text.Count(char.IsLetterOrDigit) >= 3
            ? selected
            : Empty();
    }

    private static double Score(OcrRecognitionCandidate candidate)
    {
        var score = candidate.Confidence;
        var hasHangul = candidate.Text.Any(IsHangul);
        var hasKana = candidate.Text.Any(IsKana);
        var hasHan = candidate.Text.Any(IsHan);

        if (string.Equals(candidate.Language, "ko", StringComparison.Ordinal))
        {
            if (hasHangul)
            {
                score += 0.36;
            }

            if (hasKana || (hasHan && !hasHangul))
            {
                score -= 0.18;
            }
        }
        else if (string.Equals(candidate.Language, "cjk", StringComparison.Ordinal))
        {
            if (hasKana)
            {
                score += 0.35;
            }
            else if (hasHan)
            {
                score += 0.30;
            }

            if (hasHangul)
            {
                score -= 0.25;
            }
        }

        return score;
    }

    private static bool IsHangul(char value) =>
        value is >= '\u1100' and <= '\u11ff' or
            >= '\u3130' and <= '\u318f' or
            >= '\uac00' and <= '\ud7af';

    private static bool IsKana(char value) =>
        value is >= '\u3040' and <= '\u30ff' or
            >= '\uff66' and <= '\uff9f';

    private static bool IsHan(char value) =>
        value is >= '\u3400' and <= '\u9fff' or
            >= '\uf900' and <= '\ufaff';

    private static OcrRecognitionCandidate Empty() =>
        new(string.Empty, [], "und", 0);
}
