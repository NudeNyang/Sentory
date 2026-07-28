namespace Sentory.App;

public static class AppInstanceCommandPolicy
{
    public const string RequestShutdownArgument =
        "--request-shutdown";

    public static bool IsShutdownRequest(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains(
            RequestShutdownArgument,
            StringComparer.OrdinalIgnoreCase);
    }
}
