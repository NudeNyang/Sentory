using System.IO;
using System.Text.Json;
using Sentory.Diagnostics.Uia;

namespace Sentory.Diagnostics;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.Command is DiagnosticCommand.Help)
            {
                PrintHelp();
                return 0;
            }

            var windows = WindowLocator.FindVisibleTopLevelWindows(options.ProcessNames);

            object result = options.Command switch
            {
                DiagnosticCommand.List => new
                {
                    capturedAtUtc = DateTimeOffset.UtcNow,
                    windows
                },
                DiagnosticCommand.Snapshot => await PowerShellUiaProbe.CaptureAsync(
                    options,
                    TimeSpan.FromSeconds(15)),
                DiagnosticCommand.Watch => throw new CliException(
                    "동적 watch는 정적 UIA 타당성 확인 후 PowerShell 격리 프로브로 구현할 예정이야."),
                _ => throw new InvalidOperationException("지원하지 않는 명령이야.")
            };

            var json = JsonSerializer.Serialize(result, JsonOptions);
            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                Console.WriteLine(json);
            }
            else
            {
                var fullPath = Path.GetFullPath(options.OutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, json);
                Console.WriteLine(fullPath);
            }

            return 0;
        }
        catch (CliException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintHelp();
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Sentory.Diagnostics

            사용법:
              list [--process Discord,KakaoTalk] [--output path.json]
              snapshot --process Discord [--view raw|control|content]
                       [--max-depth 20] [--max-elements 5000] [--output path.json]
              watch --process Discord [--seconds 15] [--output path.json]

            개인정보 보호:
              컨트롤 Name/Value/Text, 창 제목, 메시지 본문, 전체 실행 파일 경로는 수집하지 않아.
            """);
    }
}
