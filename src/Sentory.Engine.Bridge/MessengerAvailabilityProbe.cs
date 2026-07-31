using System.Diagnostics;
using Sentory.Core;

namespace Sentory.Engine.Bridge;

internal static class MessengerAvailabilityProbe
{
    private sealed record MessengerDefinition(
        SourceApp Source,
        string[] ProcessNames,
        Func<IReadOnlyList<string>> CandidatePaths);

    private static readonly MessengerDefinition[] Definitions =
    [
        new(
            SourceApp.Discord,
            ["Discord"],
            () => Paths(
                Local("Discord"),
                RoamingStartMenu("Discord Inc"))),
        new(
            SourceApp.KakaoTalk,
            ["KakaoTalk"],
            () => Paths(
                ProgramFilesX86("Kakao", "KakaoTalk"),
                ProgramFiles("Kakao", "KakaoTalk"),
                Local("Kakao", "KakaoTalk"))),
        new(
            SourceApp.Slack,
            ["Slack"],
            () => Paths(
                Local("slack"),
                Local("Microsoft", "WindowsApps", "slack.exe"))),
        new(
            SourceApp.WhatsApp,
            ["WhatsApp.Root", "WhatsApp"],
            () => Paths(
                Local("WhatsApp"),
                Local("Microsoft", "WindowsApps", "WhatsApp.exe"))),
        new(
            SourceApp.Telegram,
            ["Telegram"],
            () => Paths(Roaming("Telegram Desktop"))),
        new(
            SourceApp.Line,
            ["LINE"],
            () => Paths(Local("LINE"))),
        new(
            SourceApp.WeChat,
            ["Weixin", "WeChat", "WeChatAppEx"],
            () => Paths(
                ProgramFiles("Tencent", "WeChat"),
                ProgramFilesX86("Tencent", "WeChat"),
                ProgramFiles("Tencent", "Weixin"),
                ProgramFilesX86("Tencent", "Weixin")))
    ];

    public static IReadOnlyDictionary<string, bool> Detect()
    {
        var runningProcesses = ReadRunningProcessNames();
        return Detect(
            runningProcesses,
            path => File.Exists(path) || Directory.Exists(path));
    }

    internal static IReadOnlyDictionary<string, bool> Detect(
        IReadOnlySet<string> runningProcesses,
        Func<string, bool> pathExists)
    {
        ArgumentNullException.ThrowIfNull(runningProcesses);
        ArgumentNullException.ThrowIfNull(pathExists);
        return Definitions.ToDictionary(
            definition => definition.Source.ToString(),
            definition =>
                definition.ProcessNames.Any(runningProcesses.Contains) ||
                definition.CandidatePaths().Any(pathExists),
            StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> ReadRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return names;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    names.Add(process.ProcessName);
                }
                catch
                {
                }
            }
        }
        return names;
    }

    private static IReadOnlyList<string> Paths(params string?[] paths) =>
        paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();

    private static string? Local(params string[] parts) =>
        Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            parts);

    private static string? Roaming(params string[] parts) =>
        Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            parts);

    private static string? RoamingStartMenu(params string[] parts) =>
        Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            ["Microsoft", "Windows", "Start Menu", "Programs", .. parts]);

    private static string? ProgramFiles(params string[] parts) =>
        Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            parts);

    private static string? ProgramFilesX86(params string[] parts) =>
        Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86),
            parts);

    private static string? Combine(
        string root,
        IReadOnlyList<string> parts) =>
        string.IsNullOrWhiteSpace(root)
            ? null
            : Path.Combine([root, .. parts]);
}
