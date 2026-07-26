using System.IO;
using System.Management;

namespace Sentory.Platform.Windows.Runtime;

public sealed record CloudSyncFolderCandidate(
    string ProviderId,
    string ProviderName,
    string FolderPath);

public static class WindowsCloudSyncFolderDiscovery
{
    private const string SentoryFolderName = "Sentory";

    private static readonly string[] OneDriveEnvironmentVariables =
        ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    private static readonly string[] GoogleMyDriveNames =
        ["My Drive", "내 드라이브", "マイドライブ", "我的云端硬盘", "我的雲端硬碟"];

    public static IReadOnlyList<CloudSyncFolderCandidate> Discover()
    {
        var oneDriveRoots = OneDriveEnvironmentVariables
            .Select(Environment.GetEnvironmentVariable)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>();

        return Resolve(
            oneDriveRoots,
            DiscoverGoogleDriveRoots());
    }

    internal static IReadOnlyList<CloudSyncFolderCandidate> Resolve(
        IEnumerable<string> oneDriveRoots,
        IEnumerable<string> googleDriveRoots)
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
