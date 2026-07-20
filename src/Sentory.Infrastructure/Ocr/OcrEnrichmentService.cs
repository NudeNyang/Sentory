using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Ocr;

public sealed record OcrEnrichmentBatchResult(
    int Attempted,
    int Updated);

public sealed class OcrEnrichmentService(
    IImageOcrRepository repository,
    IImageTextRecognizer recognizer,
    SentoryDataPaths paths,
    Action<string, Exception>? reportFailure = null,
    IImageMetadataTitleReader? metadataTitleReader = null)
{
    public async Task<OcrEnrichmentBatchResult> EnrichBatchAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || !recognizer.IsAvailable)
        {
            return new OcrEnrichmentBatchResult(0, 0);
        }

        var candidates = await repository.GetPendingImageOcrAsync(
            recognizer.EngineName,
            limit,
            cancellationToken);
        var updated = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absolutePath = ResolveImagePath(candidate.ContentPath);
            if (absolutePath is null || !File.Exists(absolutePath))
            {
                if (await repository.UpsertImageOcrAsync(
                        new ImageOcrUpdate(
                            candidate.Sha256,
                            null,
                            string.Empty,
                            ImageOcrStatus.Failed,
                            null,
                            recognizer.EngineName,
                            DateTimeOffset.UtcNow,
                            "ImageFileUnavailable"),
                        cancellationToken))
                {
                    updated++;
                }

                continue;
            }

            ImageOcrUpdate update;
            try
            {
                var result = await recognizer.RecognizeAsync(
                    absolutePath,
                    cancellationToken);
                var text = NormalizeText(result.Text);
                var metadataTitle = metadataTitleReader?.ReadTitle(absolutePath);
                update = new ImageOcrUpdate(
                    candidate.Sha256,
                    OcrTitleGenerator.CreatePreferred(
                        metadataTitle,
                        result.PreferredTitleLines ?? result.Lines),
                    text,
                    text.Length == 0
                        ? ImageOcrStatus.NoText
                        : ImageOcrStatus.Completed,
                    result.Language,
                    result.EngineName,
                    DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                reportFailure?.Invoke(candidate.Sha256, exception);
                update = new ImageOcrUpdate(
                    candidate.Sha256,
                    null,
                    string.Empty,
                    ImageOcrStatus.Failed,
                    null,
                    recognizer.EngineName,
                    DateTimeOffset.UtcNow,
                    exception.GetType().Name);
            }

            if (await repository.UpsertImageOcrAsync(
                    update,
                    cancellationToken))
            {
                updated++;
            }
        }

        return new OcrEnrichmentBatchResult(candidates.Count, updated);
    }

    private string? ResolveImagePath(string relativePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var imagesRoot = Path.GetFullPath(paths.ImagesDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(
            Path.Combine(paths.RootDirectory, relativePath));
        return target.StartsWith(imagesRoot, comparison)
            ? target
            : null;
    }

    private static string NormalizeText(string value) =>
        string.Join(
            '\n',
            value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));
}
