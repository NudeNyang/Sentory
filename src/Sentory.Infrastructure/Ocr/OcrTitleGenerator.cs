using System.Text;
using System.Text.RegularExpressions;

namespace Sentory.Infrastructure.Ocr;

public static class OcrTitleGenerator
{
    private const int MaximumTitleLength = 60;
    private static readonly char[] InvalidFileNameCharacters =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> GenericTitles = new(
        [
            "image", "photo", "picture", "screenshot", "untitled",
            "clipboard image", "png image", "이미지", "사진", "스크린샷",
            "클립보드 이미지", "PNG 이미지", "제목 없음"
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly Regex GeneratedFileNamePattern = new(
        @"^(?:img|dsc|dscn|pxl|mvimg|screenshot|screen[\s_-]*shot|capture|photo|image|download|kakaotalk|discord)[\s_-]*\d[\d\s_.-]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? CreateFileNameCandidate(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return CreateCandidate(baseName);
    }

    public static string? CreateBestDisplayTitle(
        string? originalFileName,
        string? recognizedTitle)
    {
        var fileTitle = CreateFileNameCandidate(originalFileName);
        var ocrTitle = CreateCandidate(recognizedTitle);
        if (fileTitle is null)
        {
            return ocrTitle;
        }

        if (ocrTitle is null)
        {
            return fileTitle;
        }

        var baseName = Path.GetFileNameWithoutExtension(
            originalFileName ?? string.Empty);
        return GeneratedFileNamePattern.IsMatch(baseName)
            ? ocrTitle
            : fileTitle;
    }

    public static string? CreatePreferred(
        string? metadataTitle,
        IReadOnlyList<string> ocrLines)
    {
        var preferred = CreateCandidate(metadataTitle);
        return preferred ?? Create(ocrLines);
    }

    public static string? Create(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var candidates = lines.Take(12).ToArray();
        for (var index = 0; index < candidates.Length; index++)
        {
            var line = candidates[index];
            if (Uri.TryCreate(line.Trim(), UriKind.Absolute, out _))
            {
                continue;
            }

            var normalizedLine = Normalize(line);
            if (TryGetUnclosedJapaneseQuote(normalizedLine, out var closingQuote))
            {
                var combined = new StringBuilder(normalizedLine);
                for (var next = index + 1;
                     next < candidates.Length && next <= index + 3;
                     next++)
                {
                    combined.Append(Normalize(candidates[next]));
                    if (combined.ToString().Contains(closingQuote))
                    {
                        break;
                    }
                }

                normalizedLine = combined.ToString();
            }

            var normalized = CreateCandidate(normalizedLine);
            if (normalized is null)
            {
                continue;
            }

            return normalized.Length <= MaximumTitleLength
                ? normalized
                : normalized[..MaximumTitleLength].TrimEnd();
        }

        return null;
    }

    public static string? CreateCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Normalize(value);
        return IsUseful(normalized) ? normalized : null;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsControl(character) ||
                InvalidFileNameCharacters.Contains(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return NormalizeJapaneseLongVowelMark(
            builder.ToString().Trim(' ', '.'));
    }

    private static bool TryGetUnclosedJapaneseQuote(
        string value,
        out char closingQuote)
    {
        closingQuote = value.FirstOrDefault(character => character switch
        {
            '「' => true,
            '『' => true,
            _ => false
        }) switch
        {
            '「' => '」',
            '『' => '』',
            _ => '\0'
        };
        return closingQuote != '\0' && !value.Contains(closingQuote);
    }

    private static string NormalizeJapaneseLongVowelMark(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 1; index < characters.Length - 1; index++)
        {
            if (characters[index] == '1' &&
                IsKatakana(characters[index - 1]) &&
                IsKatakana(characters[index + 1]))
            {
                characters[index] = 'ー';
            }
        }

        return new string(characters);
    }

    private static bool IsKatakana(char value) =>
        value is >= '\u30a0' and <= '\u30ff' or
            >= '\uff66' and <= '\uff9f';

    private static bool IsUseful(string value)
    {
        if (value.Length < 3 ||
            Uri.TryCreate(value, UriKind.Absolute, out _) ||
            value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            GenericTitles.Contains(value) ||
            IsLikelyHash(value) ||
            !value.Any(char.IsLetter))
        {
            return false;
        }

        var lettersOrDigits = value.Count(char.IsLetterOrDigit);
        return lettersOrDigits >= 3 &&
               lettersOrDigits >= Math.Ceiling(value.Length * 0.45);
    }

    private static bool IsLikelyHash(string value)
    {
        var compact = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length >= 16 && compact.All(Uri.IsHexDigit);
    }
}
