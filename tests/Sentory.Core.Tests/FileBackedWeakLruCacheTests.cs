namespace Sentory.Core.Tests;

public sealed class FileBackedWeakLruCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sentory-cache-tests-{Guid.NewGuid():N}");

    public FileBackedWeakLruCacheTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void GetOrAdd_ReusesValueWhenFileIsUnchanged()
    {
        var path = CreateFile("same.bin", "first");
        var cache = new FileBackedWeakLruCache<CacheValue>(8);
        var loads = 0;

        CacheValue? Load(string _) => new(++loads);

        var first = cache.GetOrAdd(path, Load);
        var second = cache.GetOrAdd(path, Load);

        Assert.Same(first, second);
        Assert.Equal(1, loads);
    }

    [Fact]
    public void GetOrAdd_ReloadsValueWhenFileChanges()
    {
        var path = CreateFile("changed.bin", "first");
        var cache = new FileBackedWeakLruCache<CacheValue>(8);
        var loads = 0;

        CacheValue? Load(string _) => new(++loads);

        var first = cache.GetOrAdd(path, Load);
        File.AppendAllText(path, "-changed");
        var second = cache.GetOrAdd(path, Load);

        Assert.NotSame(first, second);
        Assert.Equal(2, loads);
    }

    [Fact]
    public void GetOrAdd_EvictsLeastRecentlyUsedPathAtCapacity()
    {
        var firstPath = CreateFile("first.bin", "1");
        var secondPath = CreateFile("second.bin", "2");
        var thirdPath = CreateFile("third.bin", "3");
        var cache = new FileBackedWeakLruCache<CacheValue>(2);
        var loads = 0;

        CacheValue? Load(string _) => new(++loads);

        var first = cache.GetOrAdd(firstPath, Load);
        var second = cache.GetOrAdd(secondPath, Load);
        Assert.Same(first, cache.GetOrAdd(firstPath, Load));
        var third = cache.GetOrAdd(thirdPath, Load);
        var secondReloaded = cache.GetOrAdd(secondPath, Load);

        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.NotSame(second, secondReloaded);
        Assert.Equal(4, loads);
    }

    [Fact]
    public void GetOrAdd_RemovesEntryWhenFileIsDeleted()
    {
        var path = CreateFile("deleted.bin", "value");
        var cache = new FileBackedWeakLruCache<CacheValue>(8);
        var loads = 0;

        var value = cache.GetOrAdd(path, _ => new CacheValue(++loads));
        File.Delete(path);
        var missing = cache.GetOrAdd(path, _ => new CacheValue(++loads));

        Assert.NotNull(value);
        Assert.Null(missing);
        Assert.Equal(1, loads);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string CreateFile(string fileName, string contents)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    private sealed record CacheValue(int LoadNumber);
}
