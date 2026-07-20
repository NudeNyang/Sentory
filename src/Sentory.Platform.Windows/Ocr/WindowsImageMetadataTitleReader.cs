using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;
using Sentory.Core;

namespace Sentory.Platform.Windows.Ocr;

public sealed class WindowsImageMetadataTitleReader : IImageMetadataTitleReader
{
    private static readonly string[] TitleQueries =
    [
        "/tEXt/{str=Title}",
        "/iTXt/{str=Title}",
        "/Text/Title",
        "/app1/ifd/{ushort=40091}",
        "/xmp/dc:title"
    ];

    public string? ReadTitle(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        try
        {
            using var stream = new FileStream(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat |
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            var metadata = decoder.Frames.FirstOrDefault()?.Metadata
                as BitmapMetadata;
            if (metadata is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(metadata.Title))
            {
                return metadata.Title.Trim();
            }

            foreach (var query in TitleQueries)
            {
                var title = ReadQuery(metadata, query);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title.Trim();
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  NotSupportedException or FileFormatException or
                  ArgumentException or InvalidOperationException or
                  COMException)
        {
        }

        return null;
    }

    private static string? ReadQuery(BitmapMetadata metadata, string query)
    {
        try
        {
            return ConvertToText(metadata.GetQuery(query));
        }
        catch (Exception exception)
            when (exception is NotSupportedException or ArgumentException or
                  InvalidOperationException or COMException)
        {
            return null;
        }
    }

    private static string? ConvertToText(object? value) => value switch
    {
        string text => text,
        byte[] bytes when bytes.Length > 1 => Encoding.Unicode
            .GetString(bytes)
            .TrimEnd('\0'),
        BitmapMetadata nested when !string.IsNullOrWhiteSpace(nested.Title) =>
            nested.Title,
        _ => null
    };
}
