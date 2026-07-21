using System.Collections.Specialized;
using System.Windows;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class FileDropCapturePolicyTests
{
    [Fact]
    public void DoesNotClaimDropContainingOnlyUnsupportedFiles()
    {
        using var files = new TemporaryDropFiles("document.pdf");
        var data = CreateFileDropData(files.Paths);

        var result = FileDropCapturePolicy.Inspect(data);

        Assert.False(result.ShouldObserve);
        Assert.Empty(result.ImagePaths);
    }

    [Fact]
    public void ObservesSupportedImagesAndIgnoresOtherFiles()
    {
        using var files = new TemporaryDropFiles("photo.png", "document.pdf");
        var data = CreateFileDropData(
            files.Paths[0],
            files.Paths[1],
            files.Paths[0]);

        var result = FileDropCapturePolicy.Inspect(data);

        Assert.True(result.ShouldObserve);
        Assert.Equal([files.Paths[0]], result.ImagePaths);
    }

    private static DataObject CreateFileDropData(params string[] paths)
    {
        var files = new StringCollection();
        files.AddRange(paths);
        var data = new DataObject();
        data.SetFileDropList(files);
        return data;
    }

    private sealed class TemporaryDropFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"sentory-drop-policy-{Guid.NewGuid():N}");

        public TemporaryDropFiles(params string[] names)
        {
            Directory.CreateDirectory(_directory);
            Paths = names
                .Select(name => Path.Combine(_directory, name))
                .ToArray();
            foreach (var path in Paths)
            {
                File.WriteAllBytes(path, []);
            }
        }

        public string[] Paths { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
