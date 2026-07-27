using System.IO;
using System.Management;
using System.Text.Json;

namespace Sentory.Platform.Windows.Runtime;

public sealed record CloudSyncFolderCandidate(
    string ProviderId,
    string ProviderName,
    string FolderPath)
{
    public string DisplayName => $"{ProviderName} — {FolderPath}";
}

public static class WindowsCloudSyncFolderDiscovery
{
    private const string SentoryFolderName = "Sentory";

    private static readonly string[] OneDriveEnvironmentVariables =
        ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    private static readonly string[] GoogleMyDriveNames =
        ["My Drive", "내 드라이브", "マイドライブ", "我的云端硬盘", "我的雲端硬碟"];

    private static readonly string[] MegaFolderNames = ["MEGA", "MEGAsync"];

    public static IReadOnlyList<CloudSyncFolderCandidate> Discover()
    {
        var oneDriveRoots = OneDriveEnvironmentVariables
            .Select(Environment.GetEnvironmentVariable)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>();

        return Resolve(
            oneDriveRoots,
            DiscoverGoogleDriveRoots(),
            DiscoverDropboxRoots(),
            DiscoverMegaRoots());
    }

    internal static IReadOnlyList<CloudSyncFolderCandidate> Resolve(
        IEnumerable<string> oneDriveRoots,
        IEnumerable<string> googleDriveRoots,
        IEnumerable<string>? dropboxRoots = null,
        IEnumerable<string>? megaRoots = null)
    {
        ArgumentNullException.ThrowIfNull(oneDriveRoots);
        ArgumentNullException.ThrowIfNull(googleDriveRoots);

        var candidates = new List<CloudSyncFolderCandidate>();
        AddCandidates(candidates, "onedrive", "OneDrive", oneDriveRoots);
        AddCandidates(
            candidates,
            "google-drive",
            "Google Drive",
            googleDriveRoots);
        AddCandidates(
            candidates,
            "dropbox",
            "Dropbox",
            dropboxRoots ?? []);
        AddCandidates(
            candidates,
            "mega",
            "MEGA",
            megaRoots ?? []);

        return candidates
            .DistinctBy(
                candidate => candidate.FolderPath,
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
            .OrderBy(candidate => candidate.ProviderName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.FolderPath, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string? ResolveGoogleMyDriveRoot(
        string driveRoot,
        IEnumerable<string> directoryNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driveRoot);
        ArgumentNullException.ThrowIfNull(directoryNames);

        var matchingName = directoryNames.FirstOrDefault(name =>
            GoogleMyDriveNames.Contains(
                name,
                StringComparer.OrdinalIgnoreCase));
        return matchingName is null
            ? null
            : Path.GetFullPath(Path.Combine(driveRoot, matchingName));
    }

    internal static IReadOnlyList<string> ResolveDropboxRootsFromInfoJson(
        string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return document.RootElement
                .EnumerateObject()
                .Where(property =>
                    property.Value.ValueKind == JsonValueKind.Object &&
                    property.Value.TryGetProperty("path", out var path) &&
                    path.ValueKind == JsonValueKind.String)
                .Select(property =>
                    property.Value.GetProperty("path").GetString())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> DiscoverGoogleDriveRoots()
    {
        var roots = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, VolumeName FROM Win32_LogicalDisk " +
                "WHERE DriveType = 3");
            using var results = searcher.Get();
            foreach (ManagementObject drive in results)
            {
                using (drive)
                {
                    var volumeName = drive["VolumeName"] as string;
                    var deviceId = drive["DeviceID"] as string;
                    if (!string.Equals(
                            volumeName,
                            "Google Drive",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(deviceId))
                    {
                        continue;
                    }

                    var driveRoot = string.Concat(deviceId, Path.DirectorySeparatorChar);
                    var directoryNames = Directory
                        .EnumerateDirectories(driveRoot)
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Cast<string>();
                    var root = ResolveGoogleMyDriveRoot(
                        driveRoot,
                        directoryNames);
                    if (root is not null)
                    {
                        roots.Add(root);
                    }
                }
            }
        }
        catch (Exception exception)
            when (exception is ManagementException or
                  IOException or
                  UnauthorizedAccessException or
                  InvalidOperationException or
                  NotSupportedException)
        {
            return [];
        }

        return roots;
    }

    private static IReadOnlyList<string> DiscoverDropboxRoots()
    {
        var roots = new List<string>();
        var applicationDataFolders = new[]
        {
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var applicationDataFolder in applicationDataFolders)
        {
            if (string.IsNullOrWhiteSpace(applicationDataFolder))
            {
                continue;
            }

            var infoPath = Path.Combine(
                applicationDataFolder,
                "Dropbox",
                "info.json");
            try
            {
                if (File.Exists(infoPath))
                {
                    roots.AddRange(ResolveDropboxRootsFromInfoJson(
                        File.ReadAllText(infoPath)));
                }
            }
            catch (Exception exception)
                when (exception is IOException or
                      UnauthorizedAccessException or
                      NotSupportedException)
            {
                // Try the other official info.json location.
            }
        }

        if (roots.Count > 0)
        {
            return roots;
        }

        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        return DiscoverKnownFolders(userProfile, ["Dropbox"]);
    }

    private static IReadOnlyList<string> DiscoverMegaRoots()
    {
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        return DiscoverKnownFolders(userProfile, MegaFolderNames);
    }

    private static IReadOnlyList<string> DiscoverKnownFolders(
        string parentFolder,
        IEnumerable<string> folderNames)
    {
        if (string.IsNullOrWhiteSpace(parentFolder))
        {
            return [];
        }

        return folderNames
            .Select(name => Path.Combine(parentFolder, name))
            .Where(Directory.Exists)
            .ToArray();
    }

    private static void AddCandidates(
        ICollection<CloudSyncFolderCandidate> candidates,
        string providerId,
        string providerName,
        IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) ||
                !Path.IsPathRooted(root) ||
                !Directory.Exists(root))
            {
                continue;
            }

            candidates.Add(new CloudSyncFolderCandidate(
                providerId,
                providerName,
                Path.GetFullPath(Path.Combine(root, SentoryFolderName))));
        }
    }
}
