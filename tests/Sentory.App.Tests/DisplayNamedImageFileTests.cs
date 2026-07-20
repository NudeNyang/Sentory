namespace Sentory.App.Tests;

public sealed class DisplayNamedImageFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sentory-display-image-{Guid.NewGuid():N}");

    [Fact]
    public void CreatesNamedCopyWithoutRenamingStoredImage()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, $"{new string('a', 64)}.png");
        File.WriteAllBytes(source, [1, 2, 3]);
        var openRoot = Path.Combine(_root, "opened");

        var result = DisplayNamedImageFile.Prepare(
            source,
            "VRChat_2025-01-28_23-02-56",
            new string('a', 64),
            openRoot);

        Assert.Equal(
            "VRChat_2025-01-28_23-02-56.png",
            Path.GetFileName(result));
        Assert.True(File.Exists(source));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result));
    }

    [Fact]
    public void KeepsSameDisplayNameSeparateForDifferentImages()
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, $"{new string('a', 64)}.jpg");
        var second = Path.Combine(_root, $"{new string('b', 64)}.jpg");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);
        var openRoot = Path.Combine(_root, "opened");

        var firstResult = DisplayNamedImageFile.Prepare(
            first,
            "같은 사진 제목",
            new string('a', 64),
            openRoot);
        var secondResult = DisplayNamedImageFile.Prepare(
            second,
            "같은 사진 제목",
            new string('b', 64),
            openRoot);

        Assert.Equal("같은 사진 제목.jpg", Path.GetFileName(firstResult));
        Assert.Equal("같은 사진 제목.jpg", Path.GetFileName(secondResult));
        Assert.NotEqual(
            Path.GetDirectoryName(firstResult),
            Path.GetDirectoryName(secondResult));
        Assert.Equal([1], File.ReadAllBytes(firstResult));
        Assert.Equal([2], File.ReadAllBytes(secondResult));
    }

    [Fact]
    public void RemovesCharactersThatWindowsDoesNotAllowInFileNames()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "stored.webp");
        File.WriteAllBytes(source, [4, 5]);

        var result = DisplayNamedImageFile.Prepare(
            source,
            "제목: 캐릭터/설정?",
            "content-id",
            Path.Combine(_root, "opened"));

        Assert.Equal("제목 캐릭터설정.webp", Path.GetFileName(result));
    }

    [Fact]
    public void CleanupDeletesOnlyExpiredOpenCopies()
    {
        var openRoot = Path.Combine(_root, "opened");
        Directory.CreateDirectory(openRoot);
        var expired = Path.Combine(openRoot, "expired.png");
        var recent = Path.Combine(openRoot, "recent.png");
        File.WriteAllBytes(expired, [1]);
        File.WriteAllBytes(recent, [2]);
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-8));

        DisplayNamedImageFile.CleanupOldCopies(
            TimeSpan.FromDays(7),
            openRoot,
            DateTimeOffset.UtcNow);

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(recent));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
