namespace Sentory.App.Tests;

public sealed class IntegratedFilterCountPolicyTests
{
    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(1, false, 1)]
    [InlineData(2, false, 2)]
    [InlineData(2, true, 3)]
    public void CountsEachSelectedMessengerAndDateFilter(
        int selectedMessengerCount,
        bool dateFilterActive,
        int expected)
    {
        Assert.Equal(
            expected,
            IntegratedFilterCountPolicy.Count(
                selectedMessengerCount,
                dateFilterActive));
    }
}
