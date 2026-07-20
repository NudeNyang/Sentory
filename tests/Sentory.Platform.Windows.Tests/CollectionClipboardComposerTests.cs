using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class CollectionClipboardComposerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sentory-clipboard-{Guid.NewGuid():N}");

    [Fact]
    public void CreatesOneClipboardObjectWithDeduplicatedLinksAndImages()
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.png");
        var second = Path.Combine(_root, "second.png");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);

        var data = CollectionClipboardComposer.Create(
            ["https://example.com", "https://example.com", "https://openai.com"],
            [first, first, second]);

        Assert.NotNull(data);
        Assert.Equal(
            $"https://example.com{Environment.NewLine}https://openai.com",
            data.GetText());
        Assert.Equal([first, second], data.GetFileDropList().Cast<string>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
