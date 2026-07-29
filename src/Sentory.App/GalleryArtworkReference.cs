using System.Windows.Media;

namespace Sentory.App;

public static class GalleryArtworkDecodePolicy
{
    public const int CardWidth = 384;
    public const int SiteIconWidth = 64;
    public const int DetailWidth = 480;
}

public sealed class GalleryArtworkReference
{
    private static readonly TaskScheduler BackgroundScheduler =
        new ConcurrentExclusiveSchedulerPair(
            TaskScheduler.Default,
            maxConcurrencyLevel: 1).ExclusiveScheduler;
    private readonly Lazy<ImageSource?> _image;
    private readonly Lazy<Task<ImageSource?>>? _backgroundImage;

    public GalleryArtworkReference(
        Func<ImageSource?> loader,
        bool preferBackgroundLoad = false)
    {
        ArgumentNullException.ThrowIfNull(loader);
        PreferBackgroundLoad = preferBackgroundLoad;
        _image = new Lazy<ImageSource?>(
            loader,
            LazyThreadSafetyMode.ExecutionAndPublication);
        if (preferBackgroundLoad)
        {
            _backgroundImage = new Lazy<Task<ImageSource?>>(
                () => Task.Factory.StartNew(
                    LoadAtBackgroundPriority,
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    BackgroundScheduler),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    public bool PreferBackgroundLoad { get; }

    public bool IsValueCreated => _image.IsValueCreated;

    public ImageSource? Value => _image.Value;

    public Task<ImageSource?> LoadAsync() =>
        _backgroundImage?.Value ?? Task.FromResult(Value);

    private ImageSource? LoadAtBackgroundPriority()
    {
        var thread = Thread.CurrentThread;
        var originalPriority = thread.Priority;
        try
        {
            thread.Priority = ThreadPriority.BelowNormal;
            return Value;
        }
        finally
        {
            thread.Priority = originalPriority;
        }
    }
}
