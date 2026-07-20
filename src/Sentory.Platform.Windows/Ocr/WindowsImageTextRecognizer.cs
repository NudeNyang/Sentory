using Sentory.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Sentory.Platform.Windows.Ocr;

public sealed class WindowsImageTextRecognizer : IImageTextRecognizer
{
    private const uint PreferredMaximumDimension = 1800;
    private readonly OcrEngine? _engine;

    public WindowsImageTextRecognizer()
    {
        _engine = OcrEngine.TryCreateFromUserProfileLanguages();
    }

    public bool IsAvailable => _engine is not null;

    public string EngineName => "Windows.Media.Ocr";

    public async Task<ImageTextRecognitionResult> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        var engine = _engine ?? throw new PlatformNotSupportedException(
            "Windows에 사용할 수 있는 OCR 언어가 설치되어 있지 않습니다.");

        cancellationToken.ThrowIfCancellationRequested();
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = CreateScaleTransform(decoder);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap);
        cancellationToken.ThrowIfCancellationRequested();
        var lines = result.Lines
            .Select(line => line.Text.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        return new ImageTextRecognitionResult(
            string.Join('\n', lines),
            lines,
            engine.RecognizerLanguage?.LanguageTag,
            EngineName);
    }

    private static BitmapTransform CreateScaleTransform(BitmapDecoder decoder)
    {
        var maximumDimension = Math.Min(
            PreferredMaximumDimension,
            OcrEngine.MaxImageDimension);
        var sourceMaximum = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        if (sourceMaximum <= maximumDimension)
        {
            return new BitmapTransform();
        }

        var scale = maximumDimension / (double)sourceMaximum;
        return new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = Math.Max(1, (uint)Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant
        };
    }
}
