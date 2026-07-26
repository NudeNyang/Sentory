namespace Sentory.App;

internal static class OcrRuntimePolicy
{
    internal const string DisableEnvironmentVariable =
        "SENTORY_DISABLE_OCR";

    internal static bool IsDisabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal);
}
