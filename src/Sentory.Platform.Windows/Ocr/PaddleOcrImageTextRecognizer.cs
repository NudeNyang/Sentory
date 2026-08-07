using RapidOcrNet;
using Sentory.Core;
using SkiaSharp;

namespace Sentory.Platform.Windows.Ocr;

public sealed class PaddleOcrImageTextRecognizer :
    IImageTextRecognizer,
    IDisposable
{
    public const string PaddleEngineName = "PaddleOCR.PP-OCRv5.Mobile.2026-08-r9";

    private readonly object _gate = new();
    private readonly PaddleOcrModelCache _modelCache;
    private readonly IImageTextRecognizer? _fallback;
    private RapidOcr? _koreanOcr;
    private RapidOcr? _cjkOcr;
    private bool _disposed;

    public PaddleOcrImageTextRecognizer(
        string modelCacheDirectory,
        IImageTextRecognizer? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelCacheDirectory);
        _modelCache = new PaddleOcrModelCache(modelCacheDirectory);
        _fallback = fallback;
    }

    public bool IsAvailable => true;

    public string EngineName => PaddleEngineName;

    public void ReleaseModels()
    {
        lock (_gate)
        {
            ReleaseModelsCore();
        }
    }

    public async Task<ImageTextRecognitionResult> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var selected = await Task.Run(
                () => RecognizeWithPaddle(imagePath),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new ImageTextRecognitionResult(
                selected.Text,
                selected.Lines,
                selected.Language switch
                {
                    "ko" => "ko+en",
                    "cjk" => "zh-Hans+zh-Hant+ja+en",
                    _ => selected.Language
                },
                EngineName,
                selected.PreferredTitleLines);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch when (_fallback?.IsAvailable == true)
        {
            var fallbackResult = await _fallback.RecognizeAsync(
                imagePath,
                cancellationToken);
            return fallbackResult with { EngineName = EngineName };
        }
    }

    private OcrRecognitionCandidate RecognizeWithPaddle(string imagePath)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureInitialized();
            using var bitmap = SKBitmap.Decode(imagePath)
                ?? throw new InvalidOperationException("Image decoding failed.");
            var korean = RecognizeCandidate(
                _koreanOcr!,
                bitmap,
                "ko",
                detectVerticalJapanese: false,
                joinVerticalColumns: false);
            var cjk = RecognizeCandidate(
                _cjkOcr!,
                bitmap,
                "cjk",
                detectVerticalJapanese: true,
                joinVerticalColumns: false);
            var candidates = new List<OcrRecognitionCandidate>
            {
                korean.Candidate
            };
            if (cjk.HasVerticalJapanese)
            {
                using var rotated = OcrBitmapRotation.CounterClockwise(bitmap);
                candidates.Add(RecognizeCandidate(
                    _cjkOcr!,
                    rotated,
                    "cjk",
                    detectVerticalJapanese: false,
                    joinVerticalColumns: true).Candidate);
            }
            else
            {
                candidates.Add(cjk.Candidate);
            }

            return MultilingualOcrResultSelector.Select(candidates);
        }
    }

    private void EnsureInitialized()
    {
        if (_koreanOcr is not null && _cjkOcr is not null)
        {
            return;
        }

        var paths = _modelCache.EnsureExtracted();
        var korean = CreateEngine(
            paths,
            paths.KoreanRecognition,
            paths.KoreanDictionary);
        try
        {
            var cjk = CreateEngine(
                paths,
                paths.CjkRecognition,
                paths.CjkDictionary);
            _koreanOcr = korean;
            _cjkOcr = cjk;
        }
        catch
        {
            korean.Dispose();
            throw;
        }
    }

    private static RapidOcr CreateEngine(
        PaddleOcrModelPaths paths,
        string recognitionPath,
        string dictionaryPath)
    {
        var engine = new RapidOcr();
        try
        {
            using var options = RapidOcr.GetDefaultSessionOptions(
                Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
            options.LogSeverityLevel =
                Microsoft.ML.OnnxRuntime.OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
            engine.InitModels(
                paths.Detection,
                paths.Classification,
                recognitionPath,
                dictionaryPath,
                options);
            return engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    private static CandidateRecognition RecognizeCandidate(
        RapidOcr engine,
        SKBitmap bitmap,
        string language,
        bool detectVerticalJapanese,
        bool joinVerticalColumns)
    {
        var result = engine.Detect(bitmap, RapidOcrOptions.Default);
        var detectedBlocks = result.TextBlocks
            .Where(block => !string.IsNullOrWhiteSpace(block.Text))
            .ToArray();
        var readableBlocks = detectedBlocks
            .Where(block => OcrTextBlockFilter.IsReadable(
                block.BoxPoints,
                bitmap.Width,
                bitmap.Height))
            .Select(block => new OcrDetectedTextBlock(
                block.Text.Trim(),
                block.BoxPoints,
                block.CharScores?.Average() ?? 0))
            .ToArray();
        var processed = OcrTextBlockPostProcessor.Process(
            readableBlocks,
            bitmap.Width,
            bitmap.Height,
            joinVerticalColumns);
        var lines = processed.Blocks
            .Select(block => block.Text)
            .Where(line => line.Length > 0)
            .ToArray();
        var scoreWeight = processed.Blocks.Sum(block =>
            Math.Max(1, block.Text.Length));
        var confidence = scoreWeight == 0
            ? 0
            : processed.Blocks.Sum(block =>
                block.Confidence * Math.Max(1, block.Text.Length)) /
              scoreWeight;
        var candidate = new OcrRecognitionCandidate(
            string.Join('\n', lines),
            lines,
            language,
            confidence,
            processed.PreferredTitleLines);
        var hasVerticalJapanese = detectVerticalJapanese &&
            VerticalJapaneseTextDetector.ShouldRotate(
                processed.Blocks.Select(block => new DetectedTextGeometry(
                        block.Text,
                        block.Points))
                    .ToArray(),
                bitmap.Width,
                bitmap.Height);
        return new CandidateRecognition(candidate, hasVerticalJapanese);
    }

    private sealed record CandidateRecognition(
        OcrRecognitionCandidate Candidate,
        bool HasVerticalJapanese);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseModelsCore();
            if (_fallback is IDisposable disposableFallback)
            {
                disposableFallback.Dispose();
            }
        }
    }

    private void ReleaseModelsCore()
    {
        _koreanOcr?.Dispose();
        _koreanOcr = null;
        _cjkOcr?.Dispose();
        _cjkOcr = null;
    }
}
