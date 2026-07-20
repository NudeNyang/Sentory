using System.IO;
using System.Reflection;
using System.Windows;

namespace Sentory.App;

public partial class LicenseWindow : Window
{
    private const string LicenseResourceName = "Sentory.LICENSE.txt";
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
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(LicenseResourceName);
        if (stream is null)
        {
            return "LICENSE.txt";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
