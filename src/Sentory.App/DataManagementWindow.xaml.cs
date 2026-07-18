using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.App;

public partial class DataManagementWindow : Window
{
    private static readonly CleanupOption[] AutoCleanupOptions =
    [
        new(0, "자동 정리 사용 안 함"),
        new(30, "30일 기준으로 정리"),
        new(90, "90일 기준으로 정리"),
        new(180, "180일 기준으로 정리")
    ];

    private readonly ICaptureRepository _repository;
    private readonly SentorySettingsStore _settingsStore;
    private readonly bool _isDarkTheme;
    private bool _busy;

    public DataManagementWindow(
        ICaptureRepository repository,
        SentorySettingsStore settingsStore,
        bool isDarkTheme)
    {
        InitializeComponent();
        _repository = repository;
        _settingsStore = settingsStore;
        _isDarkTheme = isDarkTheme;
        ApplyPalette();
        AutoCleanupComboBox.ItemsSource = AutoCleanupOptions;
        var savedDays = _settingsStore.Load().AutoCleanupDays;
        AutoCleanupComboBox.SelectedItem = AutoCleanupOptions.First(
            option => option.Days == savedDays);
        Loaded += async (_, _) => await RefreshStatisticsAsync();
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
    }

    public bool HasDataChanged { get; private set; }

    private async Task RefreshStatisticsAsync()
    {
        try
        {
            var statistics = await _repository.GetDataStatisticsAsync();
            TotalItemsText.Text = $"{statistics.TotalItems:N0}개";
            KindsText.Text =
                $"링크 {statistics.UrlItems:N0} · 사진 {statistics.ImageItems:N0}";
            ImageBytesText.Text = FormatBytes(statistics.ImageBytes);
            FavoriteItemsText.Text =
                $"즐겨찾기 {statistics.FavoriteItems:N0}개 보존 중";
        }
        catch (Exception)
        {
            StatusText.Text = "데이터 현황을 불러오지 못했습니다.";
        }
    }

    private async void DeleteNonFavoritesButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await ConfirmAndCleanupAsync(null, "즐겨찾기가 아닌 모든 항목");

    private void SaveAutoCleanupButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (AutoCleanupComboBox.SelectedItem is not CleanupOption option)
        {
            return;
        }

        try
        {
            var settings = _settingsStore.Load();
            settings.AutoCleanupDays = option.Days;
            settings.LastAutoCleanupAt = null;
            _settingsStore.Save(settings);
            StatusText.Text = option.Days == 0
                ? "자동 정리를 사용하지 않습니다."
                : $"{option.Days}일 기준 자동 정리를 저장했습니다.";
        }
        catch (Exception)
        {
            StatusText.Text = "자동 정리 설정을 저장하지 못했습니다.";
        }
    }

    private async Task ConfirmAndCleanupAsync(
        DateTimeOffset? olderThan,
        string targetDescription)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var preview = await _repository.PreviewCleanupAsync(olderThan);
            if (preview.TotalItems == 0)
            {
                StatusText.Text = "정리할 항목이 없습니다.";
                return;
            }

            var message =
                $"{targetDescription} {preview.TotalItems:N0}개를 삭제할까요?\n\n" +
                $"링크 {preview.UrlItems:N0}개 · 사진 {preview.ImageItems:N0}개 " +
                $"({FormatBytes(preview.ImageBytes)})\n" +
                "즐겨찾기는 삭제되지 않습니다.";
            var confirmation = System.Windows.MessageBox.Show(
                this,
                message,
                "Sentory 데이터 정리",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                StatusText.Text = "정리를 취소했습니다.";
                return;
            }

            var result = await _repository.CleanupAsync(olderThan);
            HasDataChanged = result.Deleted.TotalItems > 0;
            StatusText.Text = result.FileDeleteFailures == 0
                ? $"{result.Deleted.TotalItems:N0}개 항목을 정리했습니다."
                : $"{result.Deleted.TotalItems:N0}개를 정리했지만 일부 사진 파일은 다음 실행 때 다시 정리합니다.";
            await RefreshStatisticsAsync();
        }
        catch (Exception)
        {
            StatusText.Text = "데이터를 정리하지 못했습니다.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DeleteNonFavoritesButton.IsEnabled = !busy;
        SaveAutoCleanupButton.IsEnabled = !busy;
        AutoCleanupComboBox.IsEnabled = !busy;
        if (busy)
        {
            StatusText.Text = "삭제 대상을 확인하고 있습니다...";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:N0} {units[unit]}"
            : $"{value:N1} {units[unit]}";
    }

    private void ApplyPalette()
    {
        var palette = _isDarkTheme
            ? new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#181B1E",
                ["SurfaceBrush"] = "#202428",
                ["InputBackgroundBrush"] = "#2A2E33",
                ["TextBrush"] = "#ECEBE7",
                ["MutedTextBrush"] = "#AAA69F",
                ["LineBrush"] = "#34393F",
                ["AccentBrush"] = "#756B5E",
                ["AccentTextBrush"] = "#F5F1EA",
                ["DangerBrush"] = "#A95E57"
            }
            : new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#E9E4DC",
                ["SurfaceBrush"] = "#F7F3EC",
                ["InputBackgroundBrush"] = "#E4DED5",
                ["TextBrush"] = "#292722",
                ["MutedTextBrush"] = "#6D6861",
                ["LineBrush"] = "#CEC7BC",
                ["AccentBrush"] = "#756655",
                ["AccentTextBrush"] = "#F7F4EE",
                ["DangerBrush"] = "#B85A52"
            };
        foreach (var (key, hex) in palette)
        {
            var brush = new SolidColorBrush(
                (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Resources[key] = brush;
        }
    }

    private void ApplyTitleBarTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = _isDarkTheme ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var captionColor = ToColorRef(_isDarkTheme ? "#191C20" : "#DED6CA");
        _ = DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(int));
    }

    private static int ToColorRef(string hexColor)
    {
        var color = (System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
        return color.R | color.G << 8 | color.B << 16;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int valueSize);

    private sealed record CleanupOption(int Days, string Label);
}
