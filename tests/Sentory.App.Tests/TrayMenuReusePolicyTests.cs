namespace Sentory.App.Tests;

public sealed class TrayMenuReusePolicyTests
{
    [Fact]
    public void ReusesTheAlreadyCreatedMenu()
    {
        var created = 0;
        var first = TrayMenuReusePolicy.GetOrCreate<object>(null, () =>
        {
            created++;
            return new object();
        });
        var second = TrayMenuReusePolicy.GetOrCreate(first, () =>
        {
            created++;
            return new object();
        });

        Assert.Same(first, second);
        Assert.Equal(1, created);
    }
}
