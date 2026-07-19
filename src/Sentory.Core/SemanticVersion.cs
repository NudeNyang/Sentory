namespace Sentory.Core;

public readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string? Prerelease) : IComparable<SemanticVersion>
{
    public bool IsPrerelease => !string.IsNullOrWhiteSpace(Prerelease);

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        var parts = normalized.Split('-', 2);
        var numbers = parts[0].Split('.');
        if (numbers.Length != 3 ||
            !int.TryParse(numbers[0], out var major) ||
            !int.TryParse(numbers[1], out var minor) ||
            !int.TryParse(numbers[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(
            major,
            minor,
            patch,
            parts.Length == 2 ? parts[1] : null);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (!IsPrerelease && other.IsPrerelease) return 1;
        if (IsPrerelease && !other.IsPrerelease) return -1;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string? left, string? right)
    {
        var leftParts = (left ?? string.Empty).Split('.');
        var rightParts = (right ?? string.Empty).Split('.');
        for (var i = 0; i < Math.Max(leftParts.Length, rightParts.Length); i++)
        {
            if (i >= leftParts.Length) return -1;
            if (i >= rightParts.Length) return 1;
            var leftNumeric = int.TryParse(leftParts[i], out var leftNumber);
            var rightNumeric = int.TryParse(rightParts[i], out var rightNumber);
            var result = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : leftNumeric
                    ? -1
                    : rightNumeric
                        ? 1
                        : string.Compare(
                            leftParts[i], rightParts[i],
                            StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;
        }

        return 0;
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" +
        (IsPrerelease ? $"-{Prerelease}" : string.Empty);
}
