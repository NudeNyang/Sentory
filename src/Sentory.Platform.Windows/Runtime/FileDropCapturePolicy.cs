using System.Collections.Specialized;
using Sentory.Platform.Windows.Interop;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.IDataObject;

namespace Sentory.Platform.Windows.Runtime;

internal sealed record FileDropInspection(
    bool ShouldObserve,
    IReadOnlyList<string> ImagePaths);

internal static class FileDropCapturePolicy
{
    public static FileDropInspection Inspect(WpfDataObject data)
    {
        if (!data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            return new FileDropInspection(false, []);
        }

        var paths = data.GetData(WpfDataFormats.FileDrop) switch
        {
            string[] array => array,
            StringCollection collection => collection.Cast<string>(),
            _ => []
        };
        var imagePaths = paths
            .Where(ClipboardImageCodec.IsSupportedImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new FileDropInspection(imagePaths.Length > 0, imagePaths);
    }
}
