namespace Sentory.App.Tests;

public sealed class UpdateCheckScheduleTests
{
    [Fact]
    public void AutomaticCheckWaitsForSixHourInterval()
    {
        var now = DateTimeOffset.Parse("2026-07-22T06:00:00+00:00");

        Assert.False(UpdateCheckSchedule.ShouldCheck(
            now.AddHours(-5),
            now,
            ignoreCooldown: false));
        Assert.True(UpdateCheckSchedule.ShouldCheck(
            now.AddHours(-6),
            now,
            ignoreCooldown: false));
    }

    [Fact]
    public void ManualCheckIgnoresAutomaticCooldown()
    {
        var now = DateTimeOffset.Parse("2026-07-22T06:00:00+00:00");

        Assert.True(UpdateCheckSchedule.ShouldCheck(
            now,
            now,
            ignoreCooldown: true));
    }
}
