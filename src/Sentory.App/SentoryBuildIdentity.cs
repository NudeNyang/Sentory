using System.Reflection;

namespace Sentory.App;

internal static class SentoryBuildIdentity
{
    private const string DeveloperMarker = "+developers";

    public static string CurrentVersion
    {
        get
        {
            var value = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "0.0.0";
            return value.Split('+', 2)[0];
        }
    }

    public static bool IsDeveloperBuild =>
        IsDeveloperInformationalVersion(
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion);

    internal static bool IsDeveloperInformationalVersion(
        string? informationalVersion) =>
        informationalVersion?.Contains(
            DeveloperMarker,
            StringComparison.OrdinalIgnoreCase) == true;
}
