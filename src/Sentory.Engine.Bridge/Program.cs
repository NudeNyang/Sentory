using System.Text.Json;
using System.Text.Json.Serialization;
using Sentory.Infrastructure.Data;

namespace Sentory.Engine.Bridge;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
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

            if (command != "gallery-list")
            {
                Console.Error.WriteLine(
                    "사용법: sentory-engine health | gallery-list [limit]");
                return 2;
            }

            var limit = ParseLimit(args.ElementAtOrDefault(1));
            var paths = SentoryDataPaths.FromEnvironmentOrCurrentUser(
                Environment.GetEnvironmentVariable("SENTORY_DATA_ROOT"));
            var repository = new SqliteCaptureRepository(paths);
            await repository.InitializeAsync();
            var service = new GalleryBridgeService(repository, paths);
            await WriteJsonAsync(await service.GetGalleryAsync(limit));
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
            JsonOptions);
        await Console.Out.WriteLineAsync();
    }
}
