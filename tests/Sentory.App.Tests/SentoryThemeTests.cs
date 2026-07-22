using System.Windows;
using System.Windows.Media;

namespace Sentory.App.Tests;

public sealed class SentoryThemeTests
{
    [Fact]
    public void DarkThemeUsesAVisibleSecondaryButtonBackground()
    {
        var resources = new ResourceDictionary
        {
            ["SecondaryButtonBackgroundBrush"] =
                new SolidColorBrush(Colors.Transparent)
        };

        SentoryTheme.Apply(resources, dark: true);

        var brush = Assert.IsType<SolidColorBrush>(
            resources["SecondaryButtonBackgroundBrush"]);
        Assert.Equal(
            (Color)ColorConverter.ConvertFromString("#2D3238"),
            brush.Color);
    }

    [Fact]
    public void LightThemeKeepsTheExistingSecondaryButtonBackground()
    {
        var resources = new ResourceDictionary
        {
            ["SecondaryButtonBackgroundBrush"] =
                new SolidColorBrush(Colors.Transparent)
        };

        SentoryTheme.Apply(resources, dark: false);

        var brush = Assert.IsType<SolidColorBrush>(
            resources["SecondaryButtonBackgroundBrush"]);
        Assert.Equal(
            (Color)ColorConverter.ConvertFromString("#E4DED5"),
            brush.Color);
    }
}
