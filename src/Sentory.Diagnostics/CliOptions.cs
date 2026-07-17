using Sentory.Diagnostics.Uia;

namespace Sentory.Diagnostics;

public enum DiagnosticCommand
{
    Help,
    List,
    Snapshot,
    Watch
}

public sealed record CliOptions(
    DiagnosticCommand Command,
    IReadOnlyList<string> ProcessNames,
    UiaTreeView View,
    int MaxDepth,
    int MaxElements,
    int WatchSeconds,
    string? OutputPath)
{
    private static readonly string[] DefaultProcesses = ["Discord", "KakaoTalk"];

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            return Defaults(DiagnosticCommand.Help);
        }

        var command = args[0].ToLowerInvariant() switch
        {
            "list" => DiagnosticCommand.List,
            "snapshot" => DiagnosticCommand.Snapshot,
            "watch" => DiagnosticCommand.Watch,
            _ => throw new CliException($"알 수 없는 명령: {args[0]}")
        };

        var processNames = DefaultProcesses.ToList();
        var view = UiaTreeView.Raw;
        var maxDepth = 20;
        var maxElements = 5_000;
        var watchSeconds = 15;
        string? outputPath = null;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            string NextValue()
            {
                if (++index >= args.Length)
                {
                    throw new CliException($"{argument} 뒤에 값이 필요해.");
                }

                return args[index];
            }

            switch (argument)
            {
                case "--process":
                    processNames = NextValue()
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
                case "--view":
                    view = NextValue().ToLowerInvariant() switch
                    {
                        "raw" => UiaTreeView.Raw,
                        "control" => UiaTreeView.Control,
                        "content" => UiaTreeView.Content,
                        _ => throw new CliException("--view는 raw, control, content 중 하나여야 해.")
                    };
                    break;
                case "--max-depth":
                    maxDepth = ParseRange(NextValue(), argument, 1, 100);
                    break;
                case "--max-elements":
                    maxElements = ParseRange(NextValue(), argument, 1, 100_000);
                    break;
                case "--seconds":
                    watchSeconds = ParseRange(NextValue(), argument, 1, 300);
                    break;
                case "--output":
                    outputPath = NextValue();
                    break;
                default:
                    throw new CliException($"알 수 없는 옵션: {argument}");
            }
        }

        if (processNames.Count == 0)
        {
            throw new CliException("최소 한 개의 프로세스 이름이 필요해.");
        }

        return new CliOptions(
            command,
            processNames,
            view,
            maxDepth,
            maxElements,
            watchSeconds,
            outputPath);
    }

    private static CliOptions Defaults(DiagnosticCommand command) =>
        new(command, DefaultProcesses, UiaTreeView.Raw, 20, 5_000, 15, null);

    private static int ParseRange(string value, string option, int minimum, int maximum)
    {
        if (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum)
        {
            throw new CliException($"{option} 값은 {minimum}~{maximum} 범위의 정수여야 해.");
        }

        return parsed;
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help";
}

public sealed class CliException(string message) : Exception(message);
