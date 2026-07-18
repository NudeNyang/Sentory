namespace Sentory.Core;

public sealed record CaptureNotification(
    ContentKind Kind,
    int Count,
    DateTimeOffset CapturedAt);

public sealed record CaptureRuntimeIssue(
    string Code,
    string UserMessage,
    DateTimeOffset OccurredAt);

public interface ICaptureRuntime : IAsyncDisposable
{
    event EventHandler<CaptureNotification>? Captured;

    event EventHandler<CaptureRuntimeIssue>? IssueDetected;

    bool IsPaused { get; set; }

    void Start();
}
