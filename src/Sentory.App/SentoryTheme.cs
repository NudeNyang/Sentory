using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Sentory.Core;
using MediaColor = System.Windows.Media.Color;

namespace Sentory.App;

internal static class SentoryTheme
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["WindowBackgroundBrush"] = "#151719",
            ["HeaderBackgroundBrush"] = "#1B1E21",
            ["SurfaceBrush"] = "#202428",
            ["SurfaceSecondaryBrush"] = "#191C20",
            ["InputBackgroundBrush"] = "#24282D",
            ["SecondaryButtonBackgroundBrush"] = "#2D3238",
            ["PopupBackgroundBrush"] = "#202428",
            ["PopupHoverBrush"] = "#2A2E33",
            ["AccentBrush"] = "#AFA08E",
            ["FavoriteBrush"] = "#D4B15A",
            ["AccentTextBrush"] = "#151719",
            ["TextBrush"] = "#ECEBE7",
            ["MutedTextBrush"] = "#AAA69F",
            ["SoftTextBrush"] = "#858B93",
            ["LineBrush"] = "#32373D",
            ["CopyButtonBackgroundBrush"] = "#E6282C31",
            ["CopyButtonHoverBrush"] = "#363B41",
            ["CopyButtonBorderBrush"] = "#454B53",
            ["CardHoverBorderBrush"] = "#756B60",
            ["SelectionTintBrush"] = "#24AFA08E",
            ["SelectionBorderBrush"] = "#80AFA08E",
            ["StatusBackgroundBrush"] = "#2A2E33",
            ["StatusTextBrush"] = "#BEC2C8",
            ["NoticeBackgroundBrush"] = "#292724",
            ["NoticeBorderBrush"] = "#5A5147",
            ["SkeletonBrush"] = "#24282D",
            ["EmptyIconBackgroundBrush"] = "#292824",
            ["ToastBackgroundBrush"] = "#ECEBE7",
            ["ToastTextBrush"] = "#1A1C1F",
            ["DangerBrush"] = "#BD8078"
        };

    private static readonly IReadOnlyDictionary<string, string> WarmPalette =
        new Dictionary<string, string>
        {
            ["WindowBackgroundBrush"] = "#E9E4DC",
            ["HeaderBackgroundBrush"] = "#F2EEE7",
            ["SurfaceBrush"] = "#F7F3EC",
            ["SurfaceSecondaryBrush"] = "#DED8CF",
            ["InputBackgroundBrush"] = "#E4DED5",
            ["SecondaryButtonBackgroundBrush"] = "#E4DED5",
            ["PopupBackgroundBrush"] = "#F7F3EC",
            ["PopupHoverBrush"] = "#E4DED5",
            ["AccentBrush"] = "#756655",
            ["FavoriteBrush"] = "#B1842F",
            ["AccentTextBrush"] = "#F7F4EE",
            ["TextBrush"] = "#292722",
            ["MutedTextBrush"] = "#6D6861",
            ["SoftTextBrush"] = "#89827A",
            ["LineBrush"] = "#CEC7BC",
            ["CopyButtonBackgroundBrush"] = "#EDF3EEE7",
            ["CopyButtonHoverBrush"] = "#FAF7F1",
            ["CopyButtonBorderBrush"] = "#C8C0B5",
            ["CardHoverBorderBrush"] = "#9A8976",
            ["SelectionTintBrush"] = "#1A756655",
            ["SelectionBorderBrush"] = "#66756655",
            ["StatusBackgroundBrush"] = "#E2DDD5",
            ["StatusTextBrush"] = "#5E5952",
            ["NoticeBackgroundBrush"] = "#E8E2D7",
            ["NoticeBorderBrush"] = "#B8AA97",
            ["SkeletonBrush"] = "#D9D3CA",
            ["EmptyIconBackgroundBrush"] = "#DCE3F1",
            ["ToastBackgroundBrush"] = "#292722",
            ["ToastTextBrush"] = "#F2EEE7",
            ["DangerBrush"] = "#A6504A"
        };

    private static readonly IReadOnlyDictionary<string, MediaColor> DarkColors =
        ParsePalette(DarkPalette);

    private static readonly IReadOnlyDictionary<string, MediaColor> WarmColors =
        ParsePalette(WarmPalette);

    public static void Apply(ResourceDictionary resources, bool dark)
    {
        foreach (var (key, color) in dark ? DarkColors : WarmColors)
        {
            SetBrushColor(resources, key, color);
        }
    }

    public static void ApplyDetectionStatus(
        ResourceDictionary resources,
        CaptureRuntimeState state,
        bool dark)
    {
        var color = (state, dark) switch
        {
            (CaptureRuntimeState.Ready, false) => "#59663F",
            (CaptureRuntimeState.Ready, true) => "#A9BB89",
            (CaptureRuntimeState.ReconnectRequired, false) => "#994740",
            (CaptureRuntimeState.ReconnectRequired, true) => "#F08A82",
            (CaptureRuntimeState.Recovering, false) => "#7A4F32",
            (CaptureRuntimeState.Recovering, true) => "#D5A071",
            (_, false) => "#6F5C42",
            _ => "#C0A77D"
        };
        SetBrushColor(
            resources,
            "DiscordDetectionBrush",
            ParseColor(color));
    }

    private static IReadOnlyDictionary<string, MediaColor> ParsePalette(
        IReadOnlyDictionary<string, string> palette) =>
        palette.ToDictionary(
            pair => pair.Key,
            pair => ParseColor(pair.Value));

    private static MediaColor ParseColor(string color) =>
        (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(color);

    private static void SetBrushColor(
        ResourceDictionary resources,
        string key,
        MediaColor color)
    {
        if (resources.Contains(key) &&
            resources[key] is SolidColorBrush { IsFrozen: false } brush)
        {
            if (brush.Color != color)
            {
                brush.Color = color;
            }

            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    public static void ApplyTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var enabled = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(
            handle,
            DwmUseImmersiveDarkMode,
            ref enabled,
            sizeof(int));
        var captionColor = ToColorRef(dark ? "#191C20" : "#DED6CA");
        _ = DwmSetWindowAttribute(
            handle,
            DwmCaptionColor,
            ref captionColor,
            sizeof(int));
        var textColor = ToColorRef(dark ? "#ECEBE7" : "#292722");
        _ = DwmSetWindowAttribute(
            handle,
            DwmTextColor,
            ref textColor,
            sizeof(int));
    }

    private static int ToColorRef(string hexColor)
    {
        var color = ParseColor(hexColor);
        return color.R | color.G << 8 | color.B << 16;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int valueSize);
}
