using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentory.Core;
using Sentory.Infrastructure.Data;
using WpfClipboard = System.Windows.Clipboard;

namespace Sentory.App;

public partial class GalleryWindow : Window
{
    private readonly ICaptureRepository _repository;
    private readonly SentoryDataPaths _paths;
    private readonly SentorySettingsStore _settingsStore;
    private readonly SentorySettings _settings;
    private readonly ObservableCollection<GalleryItemViewModel> _visibleItems =
        [];
    private readonly List<GalleryItemViewModel> _allItems = [];
    private readonly HashSet<SourceApp> _sourceApps = [];
    private readonly Dictionary<string, System.Windows.Controls.Button>
        _sourceOptionButtons = [];
    private readonly Dictionary<string, System.Windows.Controls.TextBlock>
        _sourceOptionChecks = [];
    private GalleryFilter _filter = GalleryFilter.All;
    private GalleryDateRange _dateRange = GalleryDateRange.All;
    private GallerySortMode _sortMode = GallerySortMode.Newest;
    private CancellationTokenSource? _feedbackCancellation;
    private bool _loaded;
    private bool _isDarkTheme;

    public event EventHandler? DiscordRepairRequested;

    public bool IsDarkTheme => _isDarkTheme;

    public GalleryWindow(
        ICaptureRepository repository,
        SentoryDataPaths paths,
        SentorySettingsStore settingsStore)
    {
        InitializeComponent();
        _repository = repository;
        _paths = paths;
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        _sortMode = LoadSortPreference(_settings.SortMode);
        _dateRange = LoadDatePreference(_settings.FilterDateRange);
        foreach (var source in LoadSourcePreferences(
                     _settings.FilterSourceApps))
        {
            _sourceApps.Add(source);
        }
        _isDarkTheme = _settings.IsDarkTheme;
        RestoreWindowPlacement();
        ApplyTheme(_isDarkTheme);
        GalleryItems.ItemsSource = _visibleItems;
        BuildSourceOptions();
        UpdateSortControls();
        UpdateIntegratedFilterControls();
        Loaded += OnLoaded;
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (!Dispatcher.CheckAccess())
        {
            await await Dispatcher.InvokeAsync(RefreshAsync);
            return;
        }

        SetViewState(ViewState.Loading);
        try
        {
            var items = await _repository.GetRecentAsync(500);
            _allItems.Clear();
            _allItems.AddRange(items.Select(CreateViewModel));
            ApplyFilter();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            SetViewState(ViewState.Error);
        }
    }

    public void SetDiscordRepairNeeded(bool needed)
    {
        DiscordConnectionBanner.Visibility = needed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private GalleryItemViewModel CreateViewModel(CapturedItemSummary item)
    {
        var isImage = item.Kind == ContentKind.Image;
        var title = isImage
            ? "클립보드 이미지"
            : !string.IsNullOrWhiteSpace(item.PageTitle)
                ? item.PageTitle
            : string.IsNullOrWhiteSpace(item.Domain)
                ? "저장된 링크"
                : item.Domain;
        var subtitle = isImage
            ? "PNG 이미지"
            : !string.IsNullOrWhiteSpace(item.PageDescription)
                ? item.PageDescription
            : item.OriginalUrl;
        var thumbnail = isImage
            ? LoadThumbnail(item.ContentPath)
            : LoadThumbnail(item.PreviewImagePath);
        var siteIcon = isImage
            ? null
            : LoadThumbnail(item.SiteIconPath);
        return new GalleryItemViewModel(
            item,
            isImage,
            title,
            subtitle,
            $"{(isImage ? "사진" : "링크")} · " +
            GetSourceLabel(item.LastSourceApp),
            item.LastCapturedAt.LocalDateTime.ToString("M월 d일 · HH:mm"),
            item.DeliveryStatus == DeliveryStatus.NotObserved
                ? "입력 시 저장됨"
                : item.LastSourceApp == SourceApp.Discord
                    ? "Discord 전송 확인됨"
                    : "전송 확인됨",
            GetInitial(title),
            thumbnail,
            siteIcon,
            thumbnail is not null,
            siteIcon is not null,
            isImage ? Stretch.Uniform : Stretch.UniformToFill,
            isImage ? new Thickness(8) : new Thickness(0));
    }

    private ImageSource? LoadThumbnail(string? relativePath)
    {
        var absolutePath = ResolveContentPath(relativePath);
        if (absolutePath is null || !File.Exists(absolutePath))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(absolutePath, UriKind.Absolute);
            image.DecodePixelWidth = 480;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? ResolveContentPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var root = Path.GetFullPath(_paths.RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(
            Path.Combine(_paths.RootDirectory, relativePath));
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? target
            : null;
    }

    private static string GetInitial(string value)
    {
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                return char.ToUpperInvariant(character).ToString();
            }
        }

        return "S";
    }

    private void ApplyFilter()
    {
        var kind = _filter switch
        {
            GalleryFilter.Url => ContentKind.Url,
            GalleryFilter.Image => ContentKind.Image,
            _ => (ContentKind?)null
        };
        var favoritesOnly = _filter == GalleryFilter.Favorite;
        var orderedItems = GalleryQuery.Apply(
            _allItems.Select(item => item.Item),
            new GalleryQueryOptions(
                kind,
                SearchBox.Text,
                _dateRange,
                _sortMode,
                favoritesOnly,
                _sourceApps),
            DateTimeOffset.Now);
        var viewModels = _allItems.ToDictionary(
            item => item.Item.ItemId);

        _visibleItems.Clear();
        foreach (var item in orderedItems)
        {
            _visibleItems.Add(viewModels[item.ItemId]);
        }

        if (_allItems.Count > 0 && _visibleItems.Count == 0)
        {
            EmptyTitleText.Text = "검색 결과가 없습니다";
            EmptyDescriptionText.Text =
                "다른 검색어나 필터로 다시 찾아보세요.";
        }
        else
        {
            EmptyTitleText.Text = "아직 보관된 항목이 없습니다";
            EmptyDescriptionText.Text =
                "Discord에서 URL을 전송하거나 카카오톡에 URL이나 사진을 붙여넣어 보세요.";
        }

        SetViewState(
            _visibleItems.Count == 0
                ? ViewState.Empty
                : ViewState.Content);
    }

    private void SearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loaded)
        {
            ApplyFilter();
        }
    }

    private void FilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        if (sender == AllFilterButton)
        {
            _filter = GalleryFilter.All;
        }
        else if (sender == UrlFilterButton)
        {
            _filter = GalleryFilter.Url;
        }
        else if (sender == ImageFilterButton)
        {
            _filter = GalleryFilter.Image;
        }
        else
        {
            _filter = GalleryFilter.Favorite;
        }

        AllFilterButton.IsChecked = _filter == GalleryFilter.All;
        UrlFilterButton.IsChecked = _filter == GalleryFilter.Url;
        ImageFilterButton.IsChecked = _filter == GalleryFilter.Image;
        FavoriteFilterButton.IsChecked = _filter == GalleryFilter.Favorite;
        ApplyFilter();
    }

    private void FilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        var currentButton = _filter switch
        {
            GalleryFilter.All => AllFilterButton,
            GalleryFilter.Url => UrlFilterButton,
            GalleryFilter.Image => ImageFilterButton,
            GalleryFilter.Favorite => FavoriteFilterButton,
            _ => AllFilterButton
        };
        if (sender == currentButton &&
            AllFilterButton.IsChecked != true &&
            UrlFilterButton.IsChecked != true &&
            ImageFilterButton.IsChecked != true &&
            FavoriteFilterButton.IsChecked != true)
        {
            currentButton.IsChecked = true;
        }
    }

    private void IntegratedFilterButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SortPopup.IsOpen = false;
        IntegratedFilterPopup.IsOpen = !IntegratedFilterPopup.IsOpen;
    }

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        IntegratedFilterPopup.IsOpen = false;
        SortPopup.IsOpen = !SortPopup.IsOpen;
    }

    private async void DataManagementButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        IntegratedFilterPopup.IsOpen = false;
        SortPopup.IsOpen = false;
        var window = new DataManagementWindow(
            _repository,
            _settingsStore,
            _isDarkTheme)
        {
            Owner = this
        };
        window.ShowDialog();
        if (window.HasDataChanged)
        {
            await RefreshAsync();
        }
    }

    private void DateOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button
            {
                Tag: string value
            } ||
            !Enum.TryParse(value, out GalleryDateRange dateRange))
        {
            return;
        }

        _dateRange = dateRange;
        UpdateIntegratedFilterControls();
        ApplyFilter();
        SaveFilterPreferences();
    }

    private void SortOptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button
            {
                Tag: string value
            } ||
            !Enum.TryParse(value, out GallerySortMode sortMode))
        {
            return;
        }

        _sortMode = sortMode;
        SortPopup.IsOpen = false;
        UpdateSortControls();
        ApplyFilter();
        _settings.SortMode = _sortMode.ToString();
        SaveSettings("정렬 설정을 저장하지 못했습니다.");
    }

    private void SourceOptionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button
            {
                Tag: string value
            })
        {
            return;
        }

        if (value == "All")
        {
            _sourceApps.Clear();
        }
        else if (Enum.TryParse(value, out SourceApp sourceApp))
        {
            if (!_sourceApps.Add(sourceApp))
            {
                _sourceApps.Remove(sourceApp);
            }
        }

        UpdateIntegratedFilterControls();
        ApplyFilter();
        SaveFilterPreferences();
    }

    private void FilterResetButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _sourceApps.Clear();
        _dateRange = GalleryDateRange.All;
        UpdateIntegratedFilterControls();
        ApplyFilter();
        SaveFilterPreferences();
    }

    private void UpdateIntegratedFilterControls()
    {
        var activeFilterCount =
            (_sourceApps.Count > 0 ? 1 : 0) +
            (_dateRange != GalleryDateRange.All ? 1 : 0);
        FilterCountText.Text = activeFilterCount.ToString();
        FilterCountBadge.Visibility = activeFilterCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        FilterResetButton.IsEnabled = activeFilterCount > 0;
        System.Windows.Automation.AutomationProperties.SetName(
            IntegratedFilterButton,
            activeFilterCount == 0
                ? "필터"
                : $"필터 {activeFilterCount}개 적용됨");
        IntegratedFilterButton.SetResourceReference(
            System.Windows.Controls.Control.ForegroundProperty,
            activeFilterCount == 0 ? "MutedTextBrush" : "AccentBrush");

        foreach (var (value, button) in _sourceOptionButtons)
        {
            var selected = value == "All"
                ? _sourceApps.Count == 0
                : Enum.TryParse<SourceApp>(value, out var source) &&
                  _sourceApps.Contains(source);
            SetOptionButtonState(
                button,
                _sourceOptionChecks[value],
                selected);
        }

        UpdateOptionButtons(
            [
                DateAllButton,
                DateTodayButton,
                Date7DaysButton,
                Date30DaysButton
            ],
            _dateRange.ToString());
    }

    private void BuildSourceOptions()
    {
        AddSourceOption("All", "전체 메신저");
        foreach (var source in Enum.GetValues<SourceApp>())
        {
            AddSourceOption(source.ToString(), GetSourceLabel(source));
        }
    }

    private void AddSourceOption(string value, string label)
    {
        var check = new System.Windows.Controls.TextBlock
        {
            Width = 18,
            Text = "\uE73E",
            FontFamily = new System.Windows.Media.FontFamily(
                "Segoe Fluent Icons"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Hidden
        };
        var content = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };
        content.Children.Add(check);
        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });

        var button = new System.Windows.Controls.Button
        {
            Tag = value,
            Content = content,
            Style = (Style)FindResource("MenuOptionButtonStyle")
        };
        button.Click += SourceOptionButton_Click;
        SourceOptionsPanel.Children.Add(button);
        _sourceOptionButtons[value] = button;
        _sourceOptionChecks[value] = check;
    }

    private static void SetOptionButtonState(
        System.Windows.Controls.Button button,
        System.Windows.Controls.TextBlock check,
        bool selected)
    {
        button.FontWeight = selected
            ? FontWeights.SemiBold
            : FontWeights.Normal;
        button.SetResourceReference(
            System.Windows.Controls.Control.ForegroundProperty,
            selected ? "AccentBrush" : "MutedTextBrush");
        check.Visibility = selected
            ? Visibility.Visible
            : Visibility.Hidden;
    }

    private void UpdateSortControls()
    {
        SortButtonText.Text = _sortMode switch
        {
            GallerySortMode.Newest => "정렬 최신순",
            GallerySortMode.Oldest => "정렬 오래된순",
            GallerySortMode.MostCaptured => "정렬 많이 저장한 순",
            GallerySortMode.MostCopied => "정렬 많이 복사한 순",
            GallerySortMode.RecentlyCopied => "정렬 최근 복사한 순",
            GallerySortMode.Name => "정렬 이름순",
            _ => "정렬 최신순"
        };
        System.Windows.Automation.AutomationProperties.SetName(
            SortButton,
            SortButtonText.Text);
        UpdateOptionButtons(
            [
                SortNewestButton,
                SortOldestButton,
                SortMostCapturedButton,
                SortMostCopiedButton,
                SortRecentlyCopiedButton,
                SortNameButton
            ],
            _sortMode.ToString());
    }

    private static void UpdateOptionButtons(
        IEnumerable<System.Windows.Controls.Button> buttons,
        string selectedValue)
    {
        foreach (var button in buttons)
        {
            var selected = string.Equals(
                button.Tag as string,
                selectedValue,
                StringComparison.Ordinal);
            button.FontWeight = selected
                ? FontWeights.SemiBold
                : FontWeights.Normal;
            button.SetResourceReference(
                System.Windows.Controls.Control.ForegroundProperty,
                selected ? "AccentBrush" : "MutedTextBrush");
        }
    }

    private static GallerySortMode LoadSortPreference(string value) =>
        Enum.TryParse(value, out GallerySortMode sortMode)
            ? sortMode
            : GallerySortMode.Newest;

    private static GalleryDateRange LoadDatePreference(string value) =>
        Enum.TryParse(value, out GalleryDateRange dateRange)
            ? dateRange
            : GalleryDateRange.All;

    private static IEnumerable<SourceApp> LoadSourcePreferences(
        IEnumerable<string> values) =>
        values
            .Select(value => Enum.TryParse<SourceApp>(value, out var source)
                ? source
                : (SourceApp?)null)
            .OfType<SourceApp>()
            .Distinct();

    private void SaveFilterPreferences()
    {
        _settings.FilterDateRange = _dateRange.ToString();
        _settings.FilterSourceApps = _sourceApps
            .OrderBy(source => source)
            .Select(source => source.ToString())
            .ToList();
        SaveSettings("필터 설정을 저장하지 못했습니다.");
    }

    private static string GetSourceLabel(SourceApp sourceApp) =>
        sourceApp switch
        {
            SourceApp.Discord => "Discord",
            SourceApp.KakaoTalk => "카카오톡",
            _ => sourceApp.ToString()
        };

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme(_isDarkTheme);
        _settings.IsDarkTheme = _isDarkTheme;
        SaveSettings("테마 설정을 저장하지 못했습니다.");
    }

    private void ApplyTheme(bool dark)
    {
        SentoryTheme.Apply(Resources, dark);

        ThemeIcon.Text = dark ? "\uE706" : "\uE708";
        var label = dark
            ? "밝은 테마로 전환"
            : "다크 테마로 전환";
        ThemeButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(
            ThemeButton,
            label);
        ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
        => SentoryTheme.ApplyTitleBar(this, _isDarkTheme);

    private void RestoreWindowPlacement()
    {
        if (_settings.WindowLeft is not double left ||
            _settings.WindowTop is not double top ||
            _settings.WindowWidth is not double width ||
            _settings.WindowHeight is not double height ||
            !IsValidWindowPlacement(left, top, width, height))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private bool IsValidWindowPlacement(
        double left,
        double top,
        double width,
        double height)
    {
        if (!double.IsFinite(left) ||
            !double.IsFinite(top) ||
            !double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width < MinWidth ||
            height < MinHeight)
        {
            return false;
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        return left < virtualRight &&
               top < virtualBottom &&
               left + width > virtualLeft &&
               top + height > virtualTop;
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (IsValidWindowPlacement(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height))
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }

        _settings.WindowMaximized = WindowState == WindowState.Maximized;
    }

    private void SaveSettings(string? errorMessage = null)
    {
        try
        {
            var current = _settingsStore.Load();
            _settings.AutoCleanupDays = current.AutoCleanupDays;
            _settings.LastAutoCleanupAt = current.LastAutoCleanupAt;
            _settings.DiscordSupportEnabled =
                current.DiscordSupportEnabled;
            _settings.DiscordAccessibilityPrepared =
                current.DiscordAccessibilityPrepared;
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            if (errorMessage is not null)
            {
                ShowFeedback(errorMessage);
            }
        }
    }

    private async void FavoriteButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button
            {
                Tag: GalleryItemViewModel item
            })
        {
            await ToggleFavoriteAsync(item);
        }
    }

    private async void FavoriteMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem
            {
                DataContext: GalleryItemViewModel item
            })
        {
            await ToggleFavoriteAsync(item);
        }
    }

    private async Task ToggleFavoriteAsync(GalleryItemViewModel item)
    {
        var isFavorite = !item.Item.IsFavorite;
        try
        {
            if (!await _repository.SetFavoriteAsync(
                    item.Item.ItemId,
                    isFavorite))
            {
                ShowFeedback("항목을 찾지 못했습니다.");
                return;
            }

            ReplaceItem(item, item.Item with
            {
                IsFavorite = isFavorite
            });
            ShowFeedback(
                isFavorite
                    ? "즐겨찾기에 추가했습니다."
                    : "즐겨찾기에서 제거했습니다.");
        }
        catch (Exception)
        {
            ShowFeedback("즐겨찾기를 변경하지 못했습니다.");
        }
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button
            {
                Tag: GalleryItemViewModel item
            })
        {
            await CopyAsync(item);
        }
    }

    private async void CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem
            {
                DataContext: GalleryItemViewModel item
            })
        {
            await CopyAsync(item);
        }
    }

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem
            {
                DataContext: GalleryItemViewModel item
            })
        {
            OpenItem(item);
        }
    }

    private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem
            {
                DataContext: GalleryItemViewModel item
            })
        {
            await DeleteAsync(item);
        }
    }

    private void Card_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border
            {
                DataContext: GalleryItemViewModel item
            })
        {
            OpenItem(item);
        }
    }

    private async Task CopyAsync(GalleryItemViewModel item)
    {
        string successMessage;
        try
        {
            if (item.IsImage)
            {
                var path = ResolveContentPath(item.Item.ContentPath);
                if (path is null || !File.Exists(path))
                {
                    ShowFeedback("사진 파일을 찾지 못했습니다.");
                    return;
                }

                var image = LoadClipboardImage(path);
                await SetClipboardWithRetryAsync(() => WpfClipboard.SetImage(image));
                successMessage = "사진을 복사했습니다.";
            }
            else
            {
                await SetClipboardWithRetryAsync(
                    () => WpfClipboard.SetText(item.Item.OriginalUrl));
                successMessage = "URL을 복사했습니다.";
            }
        }
        catch (Exception exception)
            when (exception is COMException or ExternalException)
        {
            ShowFeedback("클립보드가 사용 중입니다. 다시 눌러 주세요.");
            return;
        }

        var copiedAt = DateTimeOffset.Now;
        try
        {
            if (await _repository.RecordCopyAsync(
                    item.Item.ItemId,
                    copiedAt))
            {
                ReplaceItem(item, item.Item with
                {
                    CopyCount = item.Item.CopyCount + 1,
                    LastCopiedAt = copiedAt
                });
            }
        }
        catch (Exception)
        {
            ShowFeedback("복사했지만 사용 기록을 저장하지 못했습니다.");
            return;
        }

        ShowFeedback(successMessage);
    }

    private void ReplaceItem(
        GalleryItemViewModel current,
        CapturedItemSummary updatedItem)
    {
        var index = _allItems.IndexOf(current);
        if (index < 0)
        {
            return;
        }

        _allItems[index] = CreateViewModel(updatedItem);
        ApplyFilter();
    }

    private static BitmapSource LoadClipboardImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static async Task SetClipboardWithRetryAsync(Action action)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 4)
            {
                await Task.Delay(35);
            }
            catch (ExternalException) when (attempt < 4)
            {
                await Task.Delay(35);
            }
        }

        action();
    }

    private void OpenItem(GalleryItemViewModel item)
    {
        var target = item.IsImage
            ? ResolveContentPath(item.Item.ContentPath)
            : item.Item.OriginalUrl;
        if (string.IsNullOrWhiteSpace(target))
        {
            ShowFeedback("원본을 찾지 못했습니다.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Win32Exception)
        {
            ShowFeedback("원본을 열지 못했습니다.");
        }
    }

    private async Task DeleteAsync(GalleryItemViewModel item)
    {
        if (!SentoryDialogWindow.Confirm(
                this,
                "항목을 삭제할까요?",
                "이 항목을 보관함에서 삭제합니다. 이 작업은 되돌릴 수 없습니다.",
                "삭제",
                _isDarkTheme,
                danger: true))
        {
            return;
        }

        try
        {
            if (await _repository.DeleteItemAsync(item.Item.ItemId))
            {
                _allItems.Remove(item);
                ApplyFilter();
                ShowFeedback("삭제했습니다.");
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            ShowFeedback("파일을 삭제하지 못했습니다.");
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync();

    private void DiscordRepairButton_Click(
        object sender,
        RoutedEventArgs e) =>
        DiscordRepairRequested?.Invoke(this, EventArgs.Empty);

    private void DiscordLaterButton_Click(
        object sender,
        RoutedEventArgs e) =>
        DiscordConnectionBanner.Visibility = Visibility.Collapsed;

    private async void ShowFeedback(string message)
    {
        _feedbackCancellation?.Cancel();
        _feedbackCancellation?.Dispose();
        _feedbackCancellation = new CancellationTokenSource();
        var token = _feedbackCancellation.Token;

        FeedbackText.Text = message;
        FeedbackToast.Visibility = Visibility.Visible;
        try
        {
            await Task.Delay(1500, token);
            FeedbackToast.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetViewState(ViewState state)
    {
        GalleryScrollViewer.Visibility =
            state == ViewState.Content ? Visibility.Visible : Visibility.Collapsed;
        LoadingPanel.Visibility =
            state == ViewState.Loading ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility =
            state == ViewState.Empty ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility =
            state == ViewState.Error ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowPlacement();
        SaveSettings();
        _feedbackCancellation?.Cancel();
        _feedbackCancellation?.Dispose();
        base.OnClosing(e);
    }

    private enum GalleryFilter
    {
        All,
        Url,
        Image,
        Favorite
    }

    private enum ViewState
    {
        Loading,
        Content,
        Empty,
        Error
    }

}

public sealed record GalleryItemViewModel(
    CapturedItemSummary Item,
    bool IsImage,
    string Title,
    string Subtitle,
    string TypeLabel,
    string DateLabel,
    string StatusLabel,
    string Initial,
    ImageSource? Thumbnail,
    ImageSource? SiteIcon,
    bool HasPrimaryArtwork,
    bool HasSiteIcon,
    Stretch ThumbnailStretch,
    Thickness ThumbnailMargin)
{
    public string Domain => Item.Domain;

    public string FavoriteIcon =>
        Item.IsFavorite ? "\uE735" : "\uE734";

    public string FavoriteToolTip =>
        Item.IsFavorite
            ? "즐겨찾기에서 제거"
            : "즐겨찾기에 추가";

    public string FavoriteMenuLabel => FavoriteToolTip;

    public bool HasBeenCopied => Item.CopyCount > 0;

    public string CopyUsageLabel => $"복사 {Item.CopyCount:N0}회";

    public string AutomationName =>
        $"{TypeLabel}, {Title}, {DateLabel}, {Item.CaptureCount}회 저장, " +
        $"{Item.CopyCount}회 복사";
}
