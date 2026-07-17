namespace Sentory.Core;

public sealed record CaptureNotification(
    ContentKind Kind,
    int Count,
    DateTimeOffset CapturedAt);

public interface ICaptureRuntime : IAsyncDisposable
{
    event EventHandler<CaptureNotification>? Captured;

    bool IsPaused { get; set; }

    void Start();
}
