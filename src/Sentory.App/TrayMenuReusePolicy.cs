namespace Sentory.App;

internal static class TrayMenuReusePolicy
{
    public static T GetOrCreate<T>(T? existing, Func<T> factory)
        where T : class => existing ?? factory();
}
