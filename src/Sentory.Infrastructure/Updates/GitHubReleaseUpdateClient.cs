using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentory.Core;

namespace Sentory.Infrastructure.Updates;

public enum UpdatePackageKind
{
    Portable,
    Installer
}

public sealed record ReleaseUpdate(
    string Version,
    string Title,
    Uri ReleasePage,
    Uri DownloadUri,
    string FileName,
    string Sha256,
    UpdatePackageKind PackageKind);

public sealed class GitHubReleaseUpdateClient : IDisposable
{
    internal const long MaximumPackageBytes = 256L * 1024 * 1024;
    private const int MaximumChecksumBytes = 8 * 1024;
    private const string ReleasesUri =
        "https://api.github.com/repos/NudeNyang/Sentory/releases?per_page=20";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public GitHubReleaseUpdateClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsClient = httpClient is null;
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Sentory", "1.0"));
        }
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseUpdate?> CheckAsync(
        string currentVersion,
        Architecture architecture,
        UpdatePackageKind packageKind,
        CancellationToken cancellationToken = default)
    {
        if (!SemanticVersion.TryParse(currentVersion, out var current))
        {
            throw new ArgumentException("Invalid current version.", nameof(currentVersion));
        }

        using var response = await _httpClient.GetAsync(
            ReleasesUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var releases = await JsonSerializer.DeserializeAsync<List<ReleaseDto>>(
            stream,
            cancellationToken: cancellationToken) ?? [];

        var candidates = releases
            .Where(release => !release.Draft)
            .Select(release => new
            {
                Release = release,
                Parsed = SemanticVersion.TryParse(release.TagName, out var parsed)
                    ? parsed
                    : (SemanticVersion?)null
            })
            .Where(candidate => candidate.Parsed is not null &&
                                candidate.Parsed.Value.CompareTo(current) > 0 &&
                                (current.IsPrerelease || !candidate.Release.Prerelease))
            .OrderByDescending(candidate => candidate.Parsed)
            .ToList();

        foreach (var candidate in candidates)
        {
            var release = candidate.Release;
            var architectureName = architecture == Architecture.Arm64
                ? "arm64"
                : "x64";
            var suffix = packageKind == UpdatePackageKind.Installer
                ? "setup.exe"
                : "portable.zip";
            var expectedName = $"Sentory-win-{architectureName}-{suffix}";
            var asset = release.Assets.FirstOrDefault(item =>
                string.Equals(item.Name, expectedName, StringComparison.OrdinalIgnoreCase));
            if (asset is null ||
                !TryCreateHttpsUri(asset.DownloadUrl, out var uri))
            {
                continue;
            }

            var hash = ParseDigest(asset.Digest) ??
                       await ReadChecksumAssetAsync(release, expectedName, cancellationToken);
            if (hash is null ||
                !TryCreateHttpsUri(release.HtmlUrl, out var releasePage))
            {
                continue;
            }

            return new ReleaseUpdate(
                candidate.Parsed.GetValueOrDefault().ToString(),
                string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                releasePage,
                uri,
                expectedName,
                hash,
                packageKind);
        }

        return null;
    }

    public async Task<string> DownloadAsync(
        ReleaseUpdate update,
        string directory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, update.FileName);
        if (File.Exists(destination))
        {
            if (new FileInfo(destination).Length <= MaximumPackageBytes &&
                await HasExpectedHashAsync(
                    destination,
                    update.Sha256,
                    cancellationToken))
            {
                return destination;
            }

            File.Delete(destination);
        }

        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await _httpClient.GetAsync(
                update.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumPackageBytes)
            {
                throw new InvalidDataException("Downloaded update is too large.");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyToLimitedAsync(
                    source,
                    output,
                    MaximumPackageBytes,
                    cancellationToken);
            }

            if (!await HasExpectedHashAsync(
                    temporary,
                    update.Sha256,
                    cancellationToken))
            {
                throw new InvalidDataException("Downloaded update hash does not match the release checksum.");
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using var downloaded = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = Convert.ToHexString(
            await SHA256.HashDataAsync(downloaded, cancellationToken));
        return string.Equals(
            actual,
            expectedHash,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> ReadChecksumAssetAsync(
        ReleaseDto release,
        string expectedName,
        CancellationToken cancellationToken)
    {
        var checksum = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, $"{expectedName}.sha256", StringComparison.OrdinalIgnoreCase));
        if (checksum is null ||
            !TryCreateHttpsUri(checksum.DownloadUrl, out var uri))
        {
            return null;
        }

        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumChecksumBytes)
        {
            return null;
        }

        await using var source = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var content = new MemoryStream();
        try
        {
            await CopyToLimitedAsync(
                source,
                content,
                MaximumChecksumBytes,
                cancellationToken);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(content.ToArray());
        var token = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return IsSha256(token) ? token!.ToLowerInvariant() : null;
    }

    private static string? ParseDigest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hash = digest[prefix.Length..];
        return IsSha256(hash) ? hash.ToLowerInvariant() : null;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool TryCreateHttpsUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            candidate.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(candidate.UserInfo))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    internal static async Task CopyToLimitedAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException("Downloaded update is too large.");
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] List<AssetDto> Assets);

    private sealed record AssetDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
