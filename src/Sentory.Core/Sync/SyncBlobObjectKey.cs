namespace Sentory.Core.Sync;

public static class SyncBlobObjectKey
{
    public const string BlobsPrefix = "blobs/sha256/";
    public const string ReadablePhotosPrefix = "photos/sha256/";
    private static readonly HashSet<string> ReadableExtensions = new(
        [".png", ".jpg", ".bmp", ".gif", ".tif", ".webp"],
        StringComparer.Ordinal);

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

    public static string CreateReadable(
        string sha256,
        string fileExtension)
    {
        if (!SyncHash.IsSha256(sha256))
        {
            throw new ArgumentException(
                "사진 SHA-256 형식이 올바르지 않습니다.",
                nameof(sha256));
        }

        var extension = NormalizeReadableExtension(fileExtension);
        return string.Concat(
            ReadablePhotosPrefix,
            sha256.ToLowerInvariant(),
            extension);
    }

    public static bool TryParseReadable(
        string? key,
        out string sha256,
        out string fileExtension)
    {
        sha256 = string.Empty;
        fileExtension = string.Empty;
        if (key is null ||
            !key.StartsWith(
                ReadablePhotosPrefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = key[ReadablePhotosPrefix.Length..];
        if (fileName.Contains('/') || fileName.Length <= 64)
        {
            return false;
        }

        var candidateSha256 = fileName[..64];
        var candidateExtension = fileName[64..];
        if (!SyncHash.IsSha256(candidateSha256) ||
            !string.Equals(
                candidateSha256,
                candidateSha256.ToLowerInvariant(),
                StringComparison.Ordinal) ||
            !ReadableExtensions.Contains(candidateExtension))
        {
            return false;
        }

        sha256 = candidateSha256;
        fileExtension = candidateExtension;
        return true;
    }

    public static string NormalizeReadableExtension(string fileExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileExtension);
        var normalized = fileExtension.ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            ".tiff" => ".tif",
            var value => value
        };
        if (!ReadableExtensions.Contains(normalized))
        {
            throw new NotSupportedException(
                $"클라우드 미리보기를 지원하지 않는 사진 확장자입니다: {fileExtension}");
        }

        return normalized;
    }
}
