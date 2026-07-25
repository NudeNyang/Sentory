namespace Sentory.Core.Sync;

public static class SyncBlobObjectKey
{
    public const string BlobsPrefix = "blobs/sha256/";

    public static string Create(string sha256)
    {
        if (!SyncHash.IsSha256(sha256))
        {
            throw new ArgumentException(
                "블롭 SHA-256 형식이 올바르지 않습니다.",
                nameof(sha256));
        }

        var normalized = sha256.ToLowerInvariant();
        return string.Concat(
            BlobsPrefix,
            normalized.AsSpan(0, 2),
            "/",
            normalized);
    }

    public static bool TryParse(string? key, out string sha256)
    {
        sha256 = string.Empty;
        if (key is null ||
            !key.StartsWith(BlobsPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = key[BlobsPrefix.Length..];
        if (suffix.Length != 2 + 1 + 64 ||
            suffix[2] != '/' ||
            !SyncHash.IsSha256(suffix[3..]) ||
            !suffix.AsSpan(0, 2).SequenceEqual(
                suffix.AsSpan(3, 2)))
        {
            return false;
        }

        sha256 = suffix[3..].ToLowerInvariant();
        return string.Equals(
            suffix[3..],
            sha256,
            StringComparison.Ordinal);
    }
}
