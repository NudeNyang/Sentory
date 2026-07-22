using Sentory.Infrastructure.Data;

namespace Sentory.App.Tests;

public sealed class SentoryThemePreferenceTests
{
    [Theory]
    [InlineData(SentoryThemeMode.Light, false, false)]
    [InlineData(SentoryThemeMode.Light, true, false)]
    [InlineData(SentoryThemeMode.Dark, false, true)]
    [InlineData(SentoryThemeMode.Dark, true, true)]
    [InlineData(SentoryThemeMode.System, false, false)]
    [InlineData(SentoryThemeMode.System, true, true)]
    public void ResolvesSelectedThemeAgainstWindowsTheme(
        SentoryThemeMode mode,
        bool windowsDark,
        bool expected)
    {
        Assert.Equal(
            expected,
            SentoryThemePreference.ResolveIsDark(mode, windowsDark));
    }

    [Theory]
    [InlineData(SentoryThemeMode.Light, false, "LightModeApplied")]
    [InlineData(SentoryThemeMode.Dark, true, "DarkModeApplied")]
    [InlineData(SentoryThemeMode.System, false, "SystemThemeApplied")]
    [InlineData(SentoryThemeMode.System, true, "SystemThemeApplied")]
    public void SelectsAppliedMessageForThemeMode(
        SentoryThemeMode mode,
        bool isDark,
        string expected)
    {
        Assert.Equal(
            expected,
            SentoryThemePreference.AppliedMessageKey(mode, isDark));
    }
}
