namespace Sentory.App;

internal static class WebGalleryRendererPolicy
{
    internal const string EnvironmentVariable =
        "SENTORY_GALLERY_RENDERER";

    public static bool IsEnabled(bool isDeveloperBuild, string? value) =>
        isDeveloperBuild &&
        string.Equals(
            value?.Trim(),
            "WebView2",
            StringComparison.OrdinalIgnoreCase);
}
