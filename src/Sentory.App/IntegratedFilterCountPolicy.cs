namespace Sentory.App;

internal static class IntegratedFilterCountPolicy
{
    public static int Count(
        int selectedMessengerCount,
        bool dateFilterActive) =>
        Math.Max(0, selectedMessengerCount) +
        (dateFilterActive ? 1 : 0);
}
