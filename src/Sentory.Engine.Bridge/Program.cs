using System.Text.Json;
using System.Text.Json.Serialization;
using Sentory.Infrastructure.Data;
using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Engine.Bridge;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains(
                    DiscordWorkerClient.WorkerArgument,
                    StringComparer.OrdinalIgnoreCase))
            {
                using var workerInput = new StreamReader(Console.OpenStandardInput());
                using var workerOutput = new StreamWriter(Console.OpenStandardOutput())
                {
                    AutoFlush = true
                };
                return await DiscordAccessibilityWorker.RunAsync(
                    workerInput,
                    workerOutput);
            }

            var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
            if (command == "health")
            {
                await WriteJsonAsync(new
                {
                    status = "ready",
                    protocolVersion = GalleryBridgeService.ProtocolVersion
                });
                return 0;
            }

            if (command == "restore-discord-startup")
            {
                new DiscordStartupRegistrationManager().Restore();
                return 0;
            }

            var paths = SentoryDataPaths.FromEnvironmentOrCurrentUser(
                Environment.GetEnvironmentVariable("SENTORY_DATA_ROOT"));
            var repository = new SqliteCaptureRepository(paths);
            await repository.InitializeAsync();
            var service = new GalleryBridgeService(repository, paths);

            if (command == "serve")
            {
                using var input = new StreamReader(Console.OpenStandardInput());
                using var output = new StreamWriter(Console.OpenStandardOutput())
                {
                    AutoFlush = true
                };
                await using var runtime = new EngineRuntimeHost(repository, paths);
                await runtime.StartAsync();
                await new BridgeServer(service, runtime).RunAsync(input, output);
                return 0;
            }

            if (command is not ("gallery-list" or "gallery-page" or
                "gallery-item" or "gallery-favorite" or "gallery-delete" or
                "gallery-copy-record"))
            {
                Console.Error.WriteLine(
                    "사용법: sentory-engine health | restore-discord-startup | serve | gallery-list [limit] | " +
                    "gallery-page <request-json> | gallery-item <id> | " +
                    "gallery-favorite <id> <true|false> | gallery-delete <ids-json> | " +
                    "gallery-copy-record <id>");
                return 2;
            }

            if (command == "gallery-page")
            {
                var request = JsonSerializer.Deserialize<GalleryPageRequestDto>(
                    args.ElementAtOrDefault(1) ?? "{}",
                    JsonOptions) ?? throw new ArgumentException(
                    "갤러리 페이지 요청을 읽지 못했습니다.");
                await WriteJsonAsync(await service.GetGalleryPageAsync(request));
            }
            else if (command == "gallery-item")
            {
                await WriteJsonAsync(await service.GetItemAsync(
                    args.ElementAtOrDefault(1) ?? string.Empty));
            }
            else if (command == "gallery-favorite")
            {
                await WriteJsonAsync(await service.SetFavoriteAsync(
                    args.ElementAtOrDefault(1) ?? string.Empty,
                    bool.Parse(args.ElementAtOrDefault(2) ?? string.Empty)));
            }
            else if (command == "gallery-delete")
            {
                var ids = JsonSerializer.Deserialize<string[]>(
                    args.ElementAtOrDefault(1) ?? "[]",
                    JsonOptions) ?? [];
                await WriteJsonAsync(await service.DeleteItemsAsync(ids));
            }
            else if (command == "gallery-copy-record")
            {
                await WriteJsonAsync(await service.RecordCopyAsync(
                    args.ElementAtOrDefault(1) ?? string.Empty));
            }
            else
            {
                var limit = ParseLimit(args.ElementAtOrDefault(1));
                await WriteJsonAsync(await service.GetGalleryAsync(limit));
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int ParseLimit(string? raw) =>
        int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 1, GalleryBridgeService.MaximumPageSize)
            : GalleryBridgeService.DefaultPageSize;

    private static async Task WriteJsonAsync<T>(T value)
    {
        await JsonSerializer.SerializeAsync(
            Console.OpenStandardOutput(),
            value,
            BridgeServer.JsonOptions);
        await Console.Out.WriteLineAsync();
    }
}
