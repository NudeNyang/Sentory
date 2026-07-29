using System.IO;
using System.Reflection;

namespace Sentory.App;

internal sealed class WebGalleryAssetStore
{
    private const string AssetVersion = "v1";
    private static readonly IReadOnlyDictionary<string, string> Assets =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["index.html"] = "Sentory.GalleryWeb.index.html",
            ["gallery.css"] = "Sentory.GalleryWeb.gallery.css",
            ["gallery.js"] = "Sentory.GalleryWeb.gallery.js"
        };

    private readonly string _assetDirectory;

    public WebGalleryAssetStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _assetDirectory = Path.Combine(
            Path.GetFullPath(dataRoot),
            "cache",
            "gallery-web",
            AssetVersion);
    }

    public string Materialize()
    {
        Directory.CreateDirectory(_assetDirectory);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var (fileName, resourceName) in Assets)
        {
            var target = Path.Combine(_assetDirectory, fileName);
            var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
            try
            {
                using var resource = assembly.GetManifestResourceStream(
                    resourceName) ?? throw new InvalidOperationException(
                    $"Embedded gallery asset is missing: {resourceName}");
                using (var output = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    resource.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }

                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        return _assetDirectory;
    }
}
