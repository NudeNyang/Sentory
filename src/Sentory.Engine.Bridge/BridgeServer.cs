using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sentory.Engine.Bridge;

public sealed class BridgeServer(
    GalleryBridgeService gallery,
    EngineRuntimeHost? runtime = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task RunAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            BridgeResponse response;
            var stopAfterResponse = false;
            try
            {
                var request = JsonSerializer.Deserialize<BridgeRequest>(
                    line,
                    JsonOptions) ?? throw new JsonException(
                    "브리지 요청이 비어 있습니다.");
                stopAfterResponse = string.Equals(
                    request.Command.Trim(),
                    "shutdown",
                    StringComparison.OrdinalIgnoreCase);
                var result = await DispatchAsync(request, cancellationToken);
                response = new BridgeResponse(request.Id, true, result, null);
            }
            catch (Exception exception)
            {
                var requestId = TryReadRequestId(line);
                response = new BridgeResponse(
                    requestId,
                    false,
                    null,
                    exception.Message);
            }

            await output.WriteLineAsync(JsonSerializer.Serialize(
                response,
                JsonOptions));
            await output.FlushAsync(cancellationToken);
            if (stopAfterResponse)
            {
                break;
            }
        }
    }

    private async Task<object?> DispatchAsync(
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        return request.Command.Trim().ToLowerInvariant() switch
        {
            "health" => new
            {
                status = "ready",
                protocolVersion = GalleryBridgeService.ProtocolVersion
            },
            "gallery-list" => await gallery.GetGalleryAsync(
                request.Payload.ValueKind == JsonValueKind.Number &&
                request.Payload.TryGetInt32(out var limit)
                    ? limit
                    : GalleryBridgeService.DefaultPageSize,
                cancellationToken),
            "gallery-revision" => await gallery.GetRevisionAsync(cancellationToken),
            "gallery-page" => await gallery.GetGalleryPageAsync(
                request.Payload.Deserialize<GalleryPageRequestDto>(JsonOptions) ??
                throw new ArgumentException("갤러리 페이지 요청을 읽지 못했습니다."),
                cancellationToken),
            "gallery-item" => await gallery.GetItemAsync(
                ReadString(request.Payload, "항목 ID"),
                cancellationToken),
            "gallery-favorite" => await DispatchFavoriteAsync(
                request.Payload,
                cancellationToken),
            "gallery-delete" => await gallery.DeleteItemsAsync(
                request.Payload.Deserialize<string[]>(JsonOptions) ?? [],
                cancellationToken),
            "gallery-copy-record" => await gallery.RecordCopyAsync(
                ReadString(request.Payload, "항목 ID"),
                cancellationToken),
            "settings-get" => RequireRuntime().GetSettings(),
            "startup-preference-get" =>
                RequireRuntime().GetStartupPreference(),
            "settings-update" => await RequireRuntime().UpdateSettingsAsync(
                request.Payload.Deserialize<EngineSettingsPatchDto>(JsonOptions) ??
                throw new ArgumentException("설정 변경 요청을 읽지 못했습니다.")),
            "sync-folder-candidates" =>
                RequireRuntime().DiscoverSyncFolders(),
            "sync-configure-folder" => await RequireRuntime()
                .ConfigureSyncFolderAsync(
                    request.Payload.Deserialize<SyncFolderRequest>(JsonOptions)
                        ?.FolderPath ?? throw new ArgumentException(
                            "동기화 폴더 경로를 읽지 못했습니다."),
                    cancellationToken),
            "sync-configure-webdav" => await DispatchSyncWebDavAsync(
                request.Payload,
                cancellationToken),
            "sync-toggle" => await RequireRuntime().ToggleSyncAsync(
                request.Payload.Deserialize<SyncToggleRequest>(JsonOptions)
                    ?.Enabled ?? throw new ArgumentException(
                        "동기화 사용 여부를 읽지 못했습니다."),
                cancellationToken),
            "runtime-poll" => RequireRuntime().Poll(),
            "runtime-pause-toggle" => await RequireRuntime().TogglePauseAsync(),
            "discord-repair" => await RequireRuntime().RepairDiscordAsync(),
            "discord-auto-repair" => await RequireRuntime().RepairDiscordAsync(
                request.Payload.Deserialize<DiscordAutoRepairRequest>(JsonOptions)
                    ?.ExpectedProcessId ?? throw new ArgumentException(
                        "Discord 프로세스 ID를 읽지 못했습니다.")),
            "data-statistics" => await RequireRuntime().GetDataStatisticsAsync(
                cancellationToken),
            "data-cleanup-preview" => await RequireRuntime().PreviewCleanupAsync(
                cancellationToken),
            "data-cleanup" => await RequireRuntime().CleanupAsync(
                cancellationToken),
            "data-directory" => RequireRuntime().GetDataDirectory(),
            "update-check" => await RequireRuntime().CheckForUpdatesAsync(
                request.Payload.Deserialize<UpdateCheckRequest>(JsonOptions)
                    ?.Manual ?? false,
                cancellationToken),
            "update-install" => RequireRuntime().InstallPreparedUpdate(
                request.Payload.Deserialize<UpdateInstallRequest>(JsonOptions)
                    ?.HostProcessId ?? throw new ArgumentException(
                        "Sentory 프로세스 ID를 읽지 못했습니다.")),
            "shutdown" => new { status = "stopping" },
            _ => throw new ArgumentException(
                $"지원하지 않는 브리지 명령입니다: {request.Command}")
        };
    }

    private EngineRuntimeHost RequireRuntime() => runtime ??
        throw new InvalidOperationException("감지 런타임을 사용할 수 없습니다.");

    private async Task<GalleryMutationDto> DispatchFavoriteAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<GalleryFavoriteRequest>(JsonOptions) ??
            throw new ArgumentException("즐겨찾기 요청을 읽지 못했습니다.");
        return await gallery.SetFavoriteAsync(
            request.ItemId,
            request.IsFavorite,
            cancellationToken);
    }

    private async Task<EngineSettingsDto> DispatchSyncWebDavAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<SyncWebDavRequest>(JsonOptions) ??
            throw new ArgumentException(
                "NAS WebDAV 설정을 읽지 못했습니다.");
        return await RequireRuntime().ConfigureSyncWebDavAsync(
            request.Endpoint,
            request.Username,
            request.Password,
            cancellationToken);
    }

    private static string ReadString(JsonElement payload, string label)
    {
        if (payload.ValueKind == JsonValueKind.String &&
            payload.GetString() is { Length: > 0 } value)
        {
            return value;
        }
        throw new ArgumentException($"{label}을(를) 읽지 못했습니다.");
    }

    private static long TryReadRequestId(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("id", out var id) &&
                   id.TryGetInt64(out var value)
                ? value
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}

public sealed record BridgeRequest(
    long Id,
    string Command,
    JsonElement Payload);

public sealed record BridgeResponse(
    long Id,
    bool Ok,
    object? Result,
    string? Error);

public sealed record GalleryFavoriteRequest(
    string ItemId,
    bool IsFavorite);

public sealed record SyncFolderRequest(string FolderPath);

public sealed record SyncWebDavRequest(
    string Endpoint,
    string? Username,
    string? Password);

public sealed record SyncToggleRequest(bool Enabled);

public sealed record DiscordAutoRepairRequest(int ExpectedProcessId);

public sealed record UpdateCheckRequest(bool Manual);

public sealed record UpdateInstallRequest(int HostProcessId);
