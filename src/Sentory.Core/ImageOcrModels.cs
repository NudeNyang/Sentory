namespace Sentory.Core;

public enum ImageOcrStatus
{
    Completed,
    NoText,
    Failed
}

public sealed record ImageOcrCandidate(
    string Sha256,
    string ContentPath);

public sealed record ImageTextRecognitionResult(
    string Text,
    IReadOnlyList<string> Lines,
    string? Language,
    string EngineName,
    IReadOnlyList<string>? PreferredTitleLines = null);

public sealed record ImageOcrUpdate(
    string Sha256,
    string? DisplayName,
    string RecognizedText,
    ImageOcrStatus Status,
    string? Language,
    string EngineName,
    DateTimeOffset ProcessedAt,
    string? ErrorCode = null);

public interface IImageTextRecognizer
{
    bool IsAvailable { get; }

    string EngineName { get; }

    Task<ImageTextRecognitionResult> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}

public interface IImageMetadataTitleReader
{
    string? ReadTitle(string imagePath);
}

public interface IImageOcrRepository
{
    Task<IReadOnlyList<ImageOcrCandidate>> GetPendingImageOcrAsync(
        string engineName,
        int limit,
        CancellationToken cancellationToken = default);

    Task<bool> UpsertImageOcrAsync(
        ImageOcrUpdate update,
        CancellationToken cancellationToken = default);
}
