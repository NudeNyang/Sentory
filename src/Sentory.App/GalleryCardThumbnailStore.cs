using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace Sentory.App;

internal sealed class GalleryCardThumbnailStore
{
    private const string CacheVersion = "v3";
    private readonly string _directory;
    private readonly Dictionary<string, object> _fileLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _fileLocksGate = new();

    public GalleryCardThumbnailStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _directory = Path.Combine(
            Path.GetFullPath(dataRoot),
            "cache",
            "gallery-card-thumbnails",
            CacheVersion);
    }

    public string? GetOrCreate(string originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        var sourcePath = Path.GetFullPath(originalPath);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        var thumbnailPath = GetCachePath(sourcePath);
        if (File.Exists(thumbnailPath))
        {
            return thumbnailPath;
        }

        object fileLock;
        lock (_fileLocksGate)
        {
            if (!_fileLocks.TryGetValue(thumbnailPath, out fileLock!))
            {
                fileLock = new object();
                _fileLocks.Add(thumbnailPath, fileLock);
            }
        }

        lock (fileLock)
        {
            if (File.Exists(thumbnailPath))
            {
                return thumbnailPath;
            }

            Directory.CreateDirectory(_directory);
            var temporaryPath = $"{thumbnailPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.UriSource = new Uri(sourcePath, UriKind.Absolute);
                image.DecodePixelWidth = GalleryArtworkDecodePolicy.CardWidth;
                image.EndInit();
                image.Freeze();

                var encoder = new JpegBitmapEncoder
                {
                    QualityLevel = 90
                };
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    encoder.Save(stream);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, thumbnailPath);
                return thumbnailPath;
            }
            catch (Exception exception)
                when (exception is IOException or
                      UnauthorizedAccessException or
                      NotSupportedException or
                      FileFormatException)
            {
                return File.Exists(thumbnailPath)
                    ? thumbnailPath
                    : null;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
    }

    public string? TryGetExisting(string originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        var sourcePath = Path.GetFullPath(originalPath);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        var thumbnailPath = GetCachePath(sourcePath);
        return File.Exists(thumbnailPath) ? thumbnailPath : null;
    }

    private string GetCachePath(string sourcePath)
    {
        var sourceInfo = new FileInfo(sourcePath);
        var cacheKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{sourcePath}|{sourceInfo.Length}|" +
                sourceInfo.LastWriteTimeUtc.Ticks))).ToLowerInvariant();
        return Path.Combine(_directory, $"{cacheKey}.jpg");
    }
}
