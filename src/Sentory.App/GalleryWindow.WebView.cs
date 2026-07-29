using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.App;

public partial class GalleryWindow
{
    private const string WebGalleryHost = "sentory-gallery.local";
    private const string WebGalleryBaseUrl =
        $"https://{WebGalleryHost}";
    private static readonly JsonSerializerOptions WebGalleryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, WebGalleryMediaSource>
        _webGalleryMedia = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _webGalleryImageGate = new(4, 4);
    private readonly CancellationTokenSource _webGalleryCancellation = new();
    private bool _useWebGallery;
    private bool _webGalleryReady;
    private bool _webGalleryInitialized;
    private bool _webGalleryFirstRangeLogged;
    private long _webGalleryRevision;
    private SentoryDiagnosticsLog? _webGalleryDiagnostics;

    private bool IsWebGalleryActive =>
        _useWebGallery && _webGalleryInitialized;

    private async Task InitializeWebGalleryAsync()
    {
        if (!_useWebGallery || _webGalleryInitialized)
        {
            return;
        }

        _webGalleryDiagnostics ??= new SentoryDiagnosticsLog(_paths);

        string assetDirectory;
        try
        {
            assetDirectory = new WebGalleryAssetStore(
                _paths.RootDirectory).Materialize();
        }
        catch (Exception exception)
        {
            FallBackToWpfGallery(
                $"WebView2 gallery assets failed: {exception}");
            return;
        }

        var indexPath = Path.Combine(assetDirectory, "index.html");
        if (!File.Exists(indexPath))
        {
            FallBackToWpfGallery(
                $"WebView2 gallery assets missing: {indexPath}");
            return;
        }

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(
                    _paths.RootDirectory,
                    "cache",
                    "webview2"));
            await GalleryWebView.EnsureCoreWebView2Async(environment);
            var core = GalleryWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled =
                SentoryBuildIdentity.IsDeveloperBuild;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                WebGalleryHost,
                assetDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.AddWebResourceRequestedFilter(
                $"{WebGalleryBaseUrl}/media/*",
                CoreWebView2WebResourceContext.All);
            core.WebMessageReceived += WebGallery_WebMessageReceived;
            core.WebResourceRequested += WebGallery_WebResourceRequested;
            core.NavigationStarting += WebGallery_NavigationStarting;
            core.NewWindowRequested += WebGallery_NewWindowRequested;
            core.ProcessFailed += WebGallery_ProcessFailed;
            _webGalleryInitialized = true;
            _webGalleryDiagnostics.Write(
                "web-gallery-initialized",
                "WebView2 gallery host initialized");
            core.Navigate($"{WebGalleryBaseUrl}/index.html");
        }
        catch (Exception exception)
        {
            FallBackToWpfGallery(
                $"WebView2 gallery initialization failed: {exception}");
        }
    }

    private void FallBackToWpfGallery(string diagnostic)
    {
        Debug.WriteLine(diagnostic);
        _webGalleryDiagnostics?.Write(
            "web-gallery-fallback",
            diagnostic);
        _useWebGallery = false;
        _webGalleryReady = false;
        _webGalleryInitialized = false;
        GalleryWebView.Visibility = System.Windows.Visibility.Collapsed;
    }

    private void WebGallery_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ||
            !string.Equals(
                uri.Host,
                WebGalleryHost,
                StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private static void WebGallery_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e) =>
        e.Handled = true;

    private void WebGallery_ProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs e)
    {
        if (_webGalleryCancellation.IsCancellationRequested)
        {
            return;
        }

        FallBackToWpfGallery(
            $"WebView2 gallery process failed: {e.ProcessFailedKind}");
        SetViewState(
            _visibleItems.Count == 0
                ? ViewState.Empty
                : ViewState.Content);
    }

    private async void WebGallery_WebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<WebGalleryClientMessage>(
                e.WebMessageAsJson,
                WebGalleryJsonOptions);
            if (message is null)
            {
                return;
            }

            if (string.Equals(
                    message.Type,
                    "ready",
                    StringComparison.OrdinalIgnoreCase))
            {
                _webGalleryReady = true;
                _webGalleryDiagnostics?.Write(
                    "web-gallery-ready",
                    "WebView2 gallery client is ready");
                PushWebGalleryReset();
                SetViewState(
                    _visibleItems.Count == 0
                        ? ViewState.Empty
                        : ViewState.Content);
                return;
            }

            if (!string.Equals(
                    message.Type,
                    "requestRange",
                    StringComparison.OrdinalIgnoreCase) ||
                message.Revision != _webGalleryRevision)
            {
                return;
            }

            await SendWebGalleryRangeAsync(message);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"WebView2 gallery message failed: {exception}");
        }
    }

    private Task SendWebGalleryRangeAsync(WebGalleryClientMessage message)
    {
        var (start, count) = WebGalleryRangePolicy.Clamp(
            message.Start,
            message.Count,
            _visibleItems.Count);
        var revision = _webGalleryRevision;
        var items = new List<WebGalleryCardDto>(count);
        for (var index = start; index < start + count; index++)
        {
            items.Add(CreateWebGalleryCard(index, _visibleItems[index], revision));
        }

        PostWebGalleryMessage(new
        {
            type = "range",
            revision,
            start,
            items
        });
        if (!_webGalleryFirstRangeLogged)
        {
            _webGalleryFirstRangeLogged = true;
            _webGalleryDiagnostics?.Write(
                "web-gallery-first-range",
                $"First viewport range sent: {start}+{count} / " +
                _visibleItems.Count);
        }
        return Task.CompletedTask;
    }

    private WebGalleryCardDto CreateWebGalleryCard(
        int index,
        GalleryItemViewModel item,
        long revision)
    {
        var artwork = WebGalleryArtworkPolicy.Resolve(item.Item);
        string? artworkUrl = null;
        if (artwork is not null)
        {
            var absolutePath = ResolveContentPath(artwork.RelativePath);
            if (absolutePath is not null && File.Exists(absolutePath))
            {
                var token = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        $"{revision}|{item.Item.ItemId:N}|" +
                        $"{absolutePath}|{artwork.CreateCardThumbnail}")))
                    .ToLowerInvariant();
                _webGalleryMedia[token] = new WebGalleryMediaSource(
                    absolutePath,
                    artwork.CreateCardThumbnail);
                artworkUrl = $"{WebGalleryBaseUrl}/media/{token}";
            }
        }

        return new WebGalleryCardDto(
            index,
            item.Item.ItemId.ToString("N"),
            item.Item.Kind.ToString(),
            item.Title,
            item.Subtitle,
            item.TypeLabel,
            item.DateLabel,
            item.StatusLabel,
            item.Domain,
            item.Initial,
            artworkUrl,
            artwork?.Mode ?? "cover",
            item.CollectionBadgeText,
            item.HasCollectionBadge,
            item.Item.IsFavorite,
            item.HasBeenCopied,
            item.CopyUsageLabel);
    }

    private async void WebGallery_WebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var uri = new Uri(e.Request.Uri);
            var token = uri.AbsolutePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (token is null || !_webGalleryMedia.TryGetValue(token, out var source))
            {
                e.Response = CreateWebGalleryResponse(
                    new MemoryStream(),
                    404,
                    "Not Found",
                    "Content-Type: text/plain\r\nCache-Control: no-store");
                return;
            }

            await _webGalleryImageGate.WaitAsync(
                _webGalleryCancellation.Token);
            try
            {
                var path = source.CreateCardThumbnail
                    ? await Task.Run(
                        () => _cardThumbnailStore.GetOrCreate(
                            source.AbsolutePath),
                        _webGalleryCancellation.Token)
                    : source.AbsolutePath;
                if (path is null || !File.Exists(path))
                {
                    e.Response = CreateWebGalleryResponse(
                        new MemoryStream(),
                        404,
                        "Not Found",
                        "Content-Type: text/plain\r\nCache-Control: no-store");
                    return;
                }

                var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                e.Response = CreateWebGalleryResponse(
                    stream,
                    200,
                    "OK",
                    $"Content-Type: {GetWebGalleryContentType(path)}\r\n" +
                    "Cache-Control: no-store");
            }
            finally
            {
                _webGalleryImageGate.Release();
            }
        }
        catch (OperationCanceledException)
            when (_webGalleryCancellation.IsCancellationRequested)
        {
            e.Response = CreateWebGalleryResponse(
                new MemoryStream(),
                503,
                "Unavailable",
                "Content-Type: text/plain\r\nCache-Control: no-store");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"WebView2 gallery media failed: {exception}");
            e.Response = CreateWebGalleryResponse(
                new MemoryStream(),
                500,
                "Failed",
                "Content-Type: text/plain\r\nCache-Control: no-store");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private CoreWebView2WebResourceResponse CreateWebGalleryResponse(
        Stream content,
        int statusCode,
        string reasonPhrase,
        string headers) =>
        GalleryWebView.CoreWebView2.Environment.CreateWebResourceResponse(
            content,
            statusCode,
            reasonPhrase,
            headers);

    private static string GetWebGalleryContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            _ => "image/jpeg"
        };

    private void PushWebGalleryReset()
    {
        if (!IsWebGalleryActive || !_webGalleryReady)
        {
            return;
        }

        _webGalleryRevision++;
        _webGalleryMedia.Clear();
        PostWebGalleryMessage(new
        {
            type = "reset",
            revision = _webGalleryRevision,
            total = _visibleItems.Count,
            theme = _isDarkTheme ? "dark" : "light"
        });
    }

    private void PushWebGalleryTheme()
    {
        if (!IsWebGalleryActive || !_webGalleryReady)
        {
            return;
        }

        PostWebGalleryMessage(new
        {
            type = "theme",
            theme = _isDarkTheme ? "dark" : "light"
        });
    }

    private void ScrollWebGalleryToTop()
    {
        if (IsWebGalleryActive && _webGalleryReady)
        {
            PostWebGalleryMessage(new { type = "scrollToTop" });
            return;
        }

        GalleryScrollViewer.ScrollToTop();
    }

    private void PostWebGalleryMessage(object message)
    {
        GalleryWebView.CoreWebView2?.PostWebMessageAsJson(
            JsonSerializer.Serialize(message, WebGalleryJsonOptions));
    }

    private void DisposeWebGallery()
    {
        if (!_webGalleryCancellation.IsCancellationRequested)
        {
            _webGalleryCancellation.Cancel();
        }

        if (GalleryWebView.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= WebGallery_WebMessageReceived;
            core.WebResourceRequested -= WebGallery_WebResourceRequested;
            core.NavigationStarting -= WebGallery_NavigationStarting;
            core.NewWindowRequested -= WebGallery_NewWindowRequested;
            core.ProcessFailed -= WebGallery_ProcessFailed;
        }

        GalleryWebView.Dispose();
        _webGalleryImageGate.Dispose();
        _webGalleryCancellation.Dispose();
    }
}
