using System.Collections.Specialized;
using System.IO;

namespace Sentory.Platform.Windows.Interop;

public static class CollectionClipboardComposer
{
    public static System.Windows.DataObject? Create(
        IEnumerable<string> urls,
        IEnumerable<string> imagePaths)
    {
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentNullException.ThrowIfNull(imagePaths);
        var uniqueUrls = urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var uniquePaths = imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (uniqueUrls.Length == 0 && uniquePaths.Length == 0)
        {
            return null;
        }

        var data = new System.Windows.DataObject();
        if (uniqueUrls.Length > 0)
        {
            data.SetText(string.Join(Environment.NewLine, uniqueUrls));
        }

        if (uniquePaths.Length > 0)
        {
            var files = new StringCollection();
            files.AddRange(uniquePaths);
            data.SetFileDropList(files);
        }

        return data;
    }
}
