using System.IO;
using System.Reflection;
using System.Windows;

namespace Sentory.App;

public partial class LicenseWindow : Window
{
    private const string LicenseResourceName = "Sentory.LICENSE.txt";
    private const string ThirdPartyNoticesResourceName =
        "Sentory.THIRD-PARTY-NOTICES.txt";
    private const string ModelProvenanceResourceName =
        "Sentory.MODEL-PROVENANCE.md";
    private readonly OverlayScrollIndicatorController _scrollIndicator;

    public LicenseWindow(bool isDarkTheme)
    {
        InitializeComponent();
        SentoryTheme.Apply(Resources, isDarkTheme);
        _scrollIndicator = new OverlayScrollIndicatorController(
            LicenseScrollViewer,
            LicenseScrollSurface,
            LicenseScrollIndicator,
            LicenseScrollIndicatorThumb,
            LicenseScrollIndicatorThumbTransform);
        LicenseText.Text = ReadLicenseText();
        SourceInitialized += (_, _) =>
            SentoryTheme.ApplyTitleBar(this, isDarkTheme);
        Closed += (_, _) => _scrollIndicator.Dispose();
    }

    private static string ReadLicenseText()
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine +
            new string('=', 72) + Environment.NewLine + Environment.NewLine,
            ReadEmbeddedText(LicenseResourceName, "LICENSE.txt"),
            ReadEmbeddedText(
                ThirdPartyNoticesResourceName,
                "THIRD-PARTY-NOTICES.txt"),
            ReadEmbeddedText(
                ModelProvenanceResourceName,
                "MODEL-PROVENANCE.md"));
    }

    private static string ReadEmbeddedText(
        string resourceName,
        string fallbackFileName)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return fallbackFileName;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
