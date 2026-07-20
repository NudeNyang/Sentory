using System.IO;
using System.Reflection;

namespace Sentory.Platform.Windows.Ocr;

internal sealed class PaddleOcrModelCache(string rootDirectory)
{
    private const string VersionDirectoryName = "ppocrv5-mobile-2026-07-r2";

    private static readonly ModelResource[] Resources =
    [
        new("Sentory.Ocr.Models.PPOCRv5.det.onnx", "det.onnx"),
        new("Sentory.Ocr.Models.PPOCRv5.cls.onnx", "cls.onnx"),
        new("Sentory.Ocr.Models.PPOCRv5.korean-rec.onnx", "korean-rec.onnx"),
        new("Sentory.Ocr.Models.PPOCRv5.cjk-rec.onnx", "cjk-rec.onnx"),
        new("Sentory.Ocr.Models.PPOCRv5.korean-dict.txt", "korean-dict.txt"),
        new("Sentory.Ocr.Models.PPOCRv5.cjk-dict.txt", "cjk-dict.txt")
    ];

    public PaddleOcrModelPaths EnsureExtracted()
    {
        var modelDirectory = Path.Combine(
            Path.GetFullPath(rootDirectory),
            VersionDirectoryName);
        Directory.CreateDirectory(modelDirectory);
        var assembly = typeof(PaddleOcrModelCache).Assembly;
        foreach (var resource in Resources)
        {
            ExtractIfNeeded(assembly, resource, modelDirectory);
        }

        return new PaddleOcrModelPaths(
            Path.Combine(modelDirectory, "det.onnx"),
            Path.Combine(modelDirectory, "cls.onnx"),
            Path.Combine(modelDirectory, "korean-rec.onnx"),
            Path.Combine(modelDirectory, "korean-dict.txt"),
            Path.Combine(modelDirectory, "cjk-rec.onnx"),
            Path.Combine(modelDirectory, "cjk-dict.txt"));
    }

    private static void ExtractIfNeeded(
        Assembly assembly,
        ModelResource resource,
        string modelDirectory)
    {
        using var stream = assembly.GetManifestResourceStream(resource.Name)
            ?? throw new InvalidOperationException(
                $"Embedded OCR model is missing: {resource.Name}");
        var targetPath = Path.Combine(modelDirectory, resource.FileName);
        if (File.Exists(targetPath) &&
            new FileInfo(targetPath).Length == stream.Length)
        {
            return;
        }

        var temporaryPath = targetPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            using (var output = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record ModelResource(string Name, string FileName);
}

internal sealed record PaddleOcrModelPaths(
    string Detection,
    string Classification,
    string KoreanRecognition,
    string KoreanDictionary,
    string CjkRecognition,
    string CjkDictionary);
