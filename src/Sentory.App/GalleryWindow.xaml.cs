using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Links;
using Sentory.Infrastructure.Ocr;
using Sentory.Platform.Windows.Interop;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using WpfClipboard = System.Windows.Clipboard;

namespace Sentory.App;

public partial class GalleryWindow : Window
{
    private const string SelectionCheckGlyph = "\uE73E";
    private const double ScrollIndicatorRevealDistance = 44;
    private const int ScrollIndicatorActiveMilliseconds = 1200;

    private readonly ICaptureRepository _repository;
    private readonly SentoryDataPaths _paths;
    private readonly SentorySettingsStore _settingsStore;
    private readonly LinkPreviewFetcher _linkPreviewFetcher;
    private readonly SentorySettings _settings;
    private readonly ResettableObservableCollection<GalleryItemViewModel>
        _visibleItems = [];
    private readonly List<GalleryItemViewModel> _allItems = [];
    private readonly HashSet<SourceApp> _sourceApps = [];
    private readonly HashSet<Guid> _selectedItemIds = [];
    private readonly Dictionary<string, System.Windows.Controls.Button>
        _sourceOptionButtons = [];
    private readonly Dictionary<string, System.Windows.Controls.TextBlock>
        _sourceOptionChecks = [];
    private readonly FileBackedWeakLruCache<ImageSource> _thumbnailCache =
        new(1024);
    private GalleryFilter _filter = GalleryFilter.All;
    private GalleryDateRange _dateRange = GalleryDateRange.All;
    private GallerySortMode _sortMode = GallerySortMode.Newest;
    private CancellationTokenSource? _feedbackCancellation;
    private CancellationTokenSource? _languageRefreshCancellation;
    private bool _loaded;
    private bool _isDarkTheme;
    private SentoryThemeMode _themeMode;
    private bool _selectionMode;
    private Point? _selectionDragStart;
    private bool _selectionDragInProgress;
    private bool _selectionDragAdditive;
    private bool _selectionDragStartedOnItem;
    private readonly HashSet<Guid> _selectionDragBaseIds = [];
    private readonly HashSet<Guid> _selectionDragPreviewIds = [];
    private readonly DispatcherTimer _scrollIndicatorHideTimer;
    private bool _scrollIndicatorNear;
    private bool _scrollIndicatorActive;
    private bool _scrollIndicatorDragging;
    private bool _scrollIndicatorShown;
    private bool _scrollIndicatorThumbEmphasized;
    private bool _discordRepairNeeded;
    private bool _detectionPaused;
    private DataManagementWindow? _dataManagementWindow;
    private bool _allowCloseWithOwnedWindows;
    private string? _availableUpdateVersion;
    private bool _updateInstallationInProgress;
    private CaptureRuntimeState _discordDetectionState =
        CaptureRuntimeState.Connecting;

    public event EventHandler? DiscordRepairRequested;

    public event EventHandler? UpdateInstallRequested;

    internal event Func<Window, Task<ManualUpdateCheckResult>>?
        ManualUpdateCheckRequested;

    public event Action<SourceApp, bool>? MessengerSupportChanged;

    public event Func<bool, bool, Task>? StartupChanged;

    public event EventHandler? LanguageChanged;

    public bool IsDarkTheme => _isDarkTheme;

    public GalleryWindow(
        ICaptureRepository repository,
        SentoryDataPaths paths,
        SentorySettingsStore settingsStore,
        LinkPreviewFetcher linkPreviewFetcher)
    {
        InitializeComponent();
        _repository = repository;
        _paths = paths;
        _settingsStore = settingsStore;
        _linkPreviewFetcher = linkPreviewFetcher;
        _settings = settingsStore.Load();
        _sortMode = LoadSortPreference(_settings.SortMode);
        _dateRange = LoadDatePreference(_settings.FilterDateRange);
        foreach (var source in LoadSourcePreferences(
                     _settings.FilterSourceApps))
        {
            _sourceApps.Add(source);
        }
        _themeMode = _settings.GetThemeMode();
        _isDarkTheme = SentoryThemePreference.ResolveIsDark(
            _themeMode,
            SentoryThemePreference.ReadWindowsIsDark());
        _settings.IsDarkTheme = _isDarkTheme;
        RestoreWindowPlacement();
        ApplyTheme(_isDarkTheme);
        GalleryItems.ItemsSource = _visibleItems;
        BuildSourceOptions();
        UpdateSortControls();
        UpdateIntegratedFilterControls();
        _scrollIndicatorHideTimer = new DispatcherTimer(
            DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(
                ScrollIndicatorActiveMilliseconds)
        };
        _scrollIndicatorHideTimer.Tick +=
            ScrollIndicatorHideTimer_Tick;
        Loaded += OnLoaded;
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        SystemEvents.UserPreferenceChanged +=
            SystemEvents_UserPreferenceChanged;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -=
            SystemEvents_UserPreferenceChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await RefreshAsync();
        await Dispatcher.InvokeAsync(
            UpdateGalleryScrollIndicator,
            DispatcherPriority.Loaded);
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

    public async Task RefreshAfterCaptureAsync()
    {
        await RefreshAsync();
        GalleryScrollViewer.ScrollToTop();
    }

    public void SetDiscordRepairNeeded(bool needed)
    {
        _discordRepairNeeded = needed;
        DiscordConnectionBanner.Visibility = needed
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateDiscordDetectionVisibility();
        _dataManagementWindow?.SetDiscordRepairNeeded(needed);
    }

    public void SetDiscordDetectionState(CaptureRuntimeState state)
    {
        _discordDetectionState = state;
        DiscordDetectionStatusText.Text =
            DiscordDetectionPresentation.GetLabel(state);
        SentoryTheme.ApplyDetectionStatus(
            Resources,
            state,
            _isDarkTheme);
        UpdateDiscordDetectionVisibility();
        _dataManagementWindow?.SetDiscordDetectionState(state);
    }

    public void SetMessengerSupportState(
        bool discordEnabled,
        bool kakaoEnabled)
    {
        _settings.DiscordSupportEnabled = discordEnabled;
        _settings.KakaoTalkSupportEnabled = kakaoEnabled;
        UpdateDiscordDetectionVisibility();
    }

    public void SetDetectionPaused(bool paused)
    {
        _detectionPaused = paused;
        _dataManagementWindow?.SetDetectionPaused(paused);
    }

    private void UpdateDiscordDetectionVisibility()
    {
        var presentation = DiscordDetectionUiPolicy.Resolve(
            _settings.DiscordSupportEnabled,
            _discordDetectionState,
            _discordRepairNeeded);
        DiscordDetectionPanel.Visibility = presentation.ShowPassiveStatus
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void SetRuntimeIssue(string? message)
    {
        RuntimeIssueText.Text = message ?? string.Empty;
        RuntimeIssueChip.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(
            RuntimeIssueChip,
            string.IsNullOrWhiteSpace(message)
                ? SentoryLocalization.Text("NoRecentIssue")
                : SentoryLocalization.Format("RecentIssueFormat", message));
    }

    public void SetAvailableUpdate(
        string? version,
        bool installationInProgress = false)
    {
        _availableUpdateVersion = version;
        _updateInstallationInProgress = installationInProgress;
        var presentation = UpdateAvailabilityUiPolicy.Resolve(
            version,
            SentoryBuildIdentity.CurrentVersion,
            installationInProgress);
        UpdateAvailableButton.Visibility = presentation.ShowInstallAction
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateAvailableButton.IsEnabled = presentation.EnableInstallAction;
        UpdateAvailableText.Text = presentation.ShowInstallAction
            ? SentoryLocalization.Format("InstallUpdateVersionFormat", version!)
            : string.Empty;
        System.Windows.Automation.AutomationProperties.SetName(
            UpdateAvailableButton,
            UpdateAvailableText.Text);
    }

    private GalleryItemViewModel CreateViewModel(CapturedItemSummary item)
    {
        var isImage = item.Kind == ContentKind.Image;
        var isCollection = item.Kind == ContentKind.Collection;
        var members = item.Members ?? [];
        var localizedText = CreateLocalizedText(item);
        var collectionImages = isCollection
            ? members
                .Where(member => member.Kind == ContentKind.Image)
                .Select(member => new GalleryImageViewModel(
                    member.ContentPath,
                    LoadThumbnail(member.ContentPath),
                    GetPhotoName(
                        member.ContentPath,
                        member.OcrDisplayName,
                        member.OriginalUrl),
                    member.Sha256))
                .Where(image => image.Thumbnail is not null)
                .ToArray()
            : [];
        var collectionArtwork = collectionImages.FirstOrDefault()?.Thumbnail;
        var collectionLinkPreview = isCollection
            ? LoadThumbnail(item.PreviewImagePath)
            : null;
        var collectionLinkIcon = isCollection
            ? LoadThumbnail(item.SiteIconPath)
            : null;
        var collectionPreview = collectionArtwork ??
            collectionLinkPreview ??
            collectionLinkIcon;
        var collectionUsesSiteIcon = isCollection &&
            collectionArtwork is null &&
            collectionLinkPreview is null &&
            collectionLinkIcon is not null;
        var thumbnail = isCollection
            ? collectionPreview
            : isImage
                ? LoadThumbnail(item.ContentPath)
                : LoadThumbnail(item.PreviewImagePath);
        var siteIcon = isImage || isCollection
            ? null
            : LoadThumbnail(item.SiteIconPath);
        return new GalleryItemViewModel(
            item,
            isImage,
            isCollection,
            localizedText.Title,
            localizedText.Subtitle,
            localizedText.TypeLabel,
            localizedText.DateLabel,
            localizedText.StatusLabel,
            localizedText.Initial,
            thumbnail,
            siteIcon,
            thumbnail is not null,
            siteIcon is not null,
            isImage || collectionArtwork is not null || collectionUsesSiteIcon
                ? Stretch.Uniform
                : Stretch.UniformToFill,
            isImage || collectionArtwork is not null
                ? new Thickness(8)
                : collectionUsesSiteIcon
                    ? new Thickness(72)
                    : new Thickness(0),
            localizedText.CollectionBadgeText,
            isCollection,
            collectionImages,
            new GalleryItemSelectionState(
                _selectionMode,
                _selectedItemIds.Contains(item.ItemId)));
    }

    private static GalleryItemLocalizedText CreateLocalizedText(
        CapturedItemSummary item)
    {
        var isImage = item.Kind == ContentKind.Image;
        var isCollection = item.Kind == ContentKind.Collection;
        var members = item.Members ?? [];
        var imageCount = members.Count(member => member.Kind == ContentKind.Image);
        var urlCount = members.Count(member => member.Kind == ContentKind.Url);
        var imageTitle = isImage
            ? OcrTitleGenerator.CreateBestDisplayTitle(
                item.OriginalUrl,
                item.OcrDisplayName)
            : null;
        var title = isCollection
            ? SentoryLocalization.Format("CollectionTitleFormat", imageCount, urlCount)
            : isImage
            ? imageTitle ?? SentoryLocalization.Text("ClipboardImage")
            : !string.IsNullOrWhiteSpace(item.PageTitle)
                ? item.PageTitle
            : string.IsNullOrWhiteSpace(item.Domain)
                ? SentoryLocalization.Text("SavedLink")
                : item.Domain;
        var subtitle = isCollection
            ? string.Join(" · ", members
                .Where(member => member.Kind == ContentKind.Url)
                .Select(member => member.Domain)
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Take(2)) is { Length: > 0 } domains
                    ? domains
                    : SentoryLocalization.Format("CollectionItemsFormat", members.Count)
            : isImage
            ? !string.IsNullOrWhiteSpace(item.OcrText)
                ? CreateOcrSnippet(item.OcrText)
                : SentoryLocalization.Format(
                    "ImageFormatFormat",
                    GetImageFormatLabel(item))
            : !string.IsNullOrWhiteSpace(item.PageDescription)
                ? item.PageDescription
            : item.OriginalUrl;
        return new GalleryItemLocalizedText(
            title,
            subtitle,
            $"{SentoryLocalization.Text(isCollection ? "Collection" : isImage ? "Image" : "Link")} · " +
            GetSourceLabel(item.LastSourceApp),
            SentoryLocalization.FormatDate(item.LastCapturedAt.LocalDateTime),
            item.DeliveryStatus == DeliveryStatus.NotObserved
                ? SentoryLocalization.Text("SavedOnInput")
                : item.LastSourceApp == SourceApp.Discord
                    ? SentoryLocalization.Text("DiscordSent")
                    : SentoryLocalization.Text("SentConfirmed"),
            GetInitial(isCollection && urlCount > 0
                ? members.First(member => member.Kind == ContentKind.Url).Domain
                : title),
            isCollection
                ? SentoryLocalization.Format("CollectionItemsFormat", members.Count)
                : string.Empty);
    }

    private ImageSource? LoadThumbnail(string? relativePath)
    {
        var absolutePath = ResolveContentPath(relativePath);
        if (absolutePath is null || !File.Exists(absolutePath))
        {
            return null;
        }

        return _thumbnailCache.GetOrAdd(
            absolutePath,
            LoadThumbnailFromFile);
    }

    private static ImageSource? LoadThumbnailFromFile(string absolutePath)
    {
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

    private static string GetImageFormatLabel(CapturedItemSummary item)
    {
        var extension = Path.GetExtension(item.ContentPath);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.TrimStart('.').ToUpperInvariant();
        }

        return item.MimeType?.Split('/').LastOrDefault()?.ToUpperInvariant()
            ?? "PNG";
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

        _visibleItems.ReplaceAll(orderedItems.Select(
            item => viewModels[item.ItemId]));

        UpdateSelectionControls();

        UpdateEmptyStateText();

        SetViewState(
            _visibleItems.Count == 0
                ? ViewState.Empty
                : ViewState.Content);
    }

    private void UpdateEmptyStateText()
    {
        if (_allItems.Count > 0 && _visibleItems.Count == 0)
        {
            EmptyTitleText.Text = SentoryLocalization.Text("NoSearchResults");
            EmptyDescriptionText.Text = SentoryLocalization.Text(
                "NoSearchResultsDescription");
            return;
        }

        EmptyTitleText.Text = SentoryLocalization.Text("NoItems");
        EmptyDescriptionText.Text =
            SentoryLocalization.Text("NoItemsDescription");
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

        var nextFilter = sender == AllFilterButton
            ? GalleryFilter.All
            : sender == UrlFilterButton
                ? GalleryFilter.Url
                : sender == ImageFilterButton
                    ? GalleryFilter.Image
                    : GalleryFilter.Favorite;
        if (_filter == nextFilter)
        {
            return;
        }

        _filter = nextFilter;
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
            _paths,
            _discordDetectionState,
            _discordRepairNeeded,
            _detectionPaused)
        {
            Owner = this
        };
        _dataManagementWindow = window;
        window.ThemeSelectionChanged += ApplyThemeSelection;
        window.LanguageSelectionChanged += ApplyLanguageSelection;
        window.MessengerSupportSelectionChanged +=
            ApplyMessengerSupportSelection;
        window.StartupSelectionChanged += ApplyStartupSelection;
        window.DataChanged += RefreshAsync;
        Func<Task<ManualUpdateCheckResult>> updateCheckHandler =
            () => RequestManualUpdateCheckAsync(window);
        window.UpdateCheckRequested += updateCheckHandler;
        try
        {
            var closed = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult(true);
            window.Show();
            await closed.Task;
            RestoreAfterOwnedWindowClosed();
        }
        finally
        {
            if (ReferenceEquals(_dataManagementWindow, window))
            {
                _dataManagementWindow = null;
            }

            window.ThemeSelectionChanged -= ApplyThemeSelection;
            window.LanguageSelectionChanged -= ApplyLanguageSelection;
            window.MessengerSupportSelectionChanged -=
                ApplyMessengerSupportSelection;
            window.StartupSelectionChanged -= ApplyStartupSelection;
            window.DataChanged -= RefreshAsync;
            window.UpdateCheckRequested -= updateCheckHandler;
        }

        if (window.ThemeChanged)
        {
            var settings = _settingsStore.Load();
            _themeMode = settings.GetThemeMode();
            _isDarkTheme = SentoryThemePreference.ResolveIsDark(
                _themeMode,
                SentoryThemePreference.ReadWindowsIsDark());
            _settings.ThemeMode = _themeMode.ToString();
            _settings.IsDarkTheme = _isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }

        if (window.DiscordRepairRequested)
        {
            DiscordRepairRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<ManualUpdateCheckResult>
        RequestManualUpdateCheckAsync(Window owner)
    {
        var handler = ManualUpdateCheckRequested;
        return handler is null
            ? new ManualUpdateCheckResult(ManualUpdateCheckOutcome.Failed)
            : await handler(owner);
    }

    private async Task ApplyLanguageSelection(string language)
    {
        _languageRefreshCancellation?.Cancel();
        _languageRefreshCancellation?.Dispose();
        _languageRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = _languageRefreshCancellation.Token;
        try
        {
            _settings.Language = language;
            SentoryLocalization.SetLanguage(language);
            SentoryLocalization.ApplyCurrent(Resources);
            RebuildLocalizedShellControls();

            var virtualizingPanel = FindVisualDescendant<
                VirtualizingCenteredWrapPanel>(GalleryItems);
            IReadOnlyList<GalleryItemViewModel> visibleItems =
                virtualizingPanel is null
                    ? []
                    : virtualizingPanel
                        .GetVisibleDataItems()
                        .OfType<GalleryItemViewModel>()
                        .ToArray();
            var batches = LanguageRefreshPlan.Create<GalleryItemViewModel>(
                _allItems,
                visibleItems,
                backgroundBatchSize: 24,
                ReferenceEqualityComparer.Instance);
            if (batches.Count == 0)
            {
                UpdateSelectionControls();
                UpdateEmptyStateText();
                await Dispatcher.Yield(DispatcherPriority.Render);
            }

            for (var batchIndex = 0;
                 batchIndex < batches.Count;
                 batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var item in batches[batchIndex])
                {
                    item.ApplyLocalizedText(CreateLocalizedText(item.Item));
                }

                if (batchIndex == 0)
                {
                    UpdateSelectionControls();
                    UpdateEmptyStateText();
                    await Dispatcher.Yield(DispatcherPriority.Render);
                }
                else
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ApplyMessengerSupportSelection(
        SourceApp sourceApp,
        bool enabled)
    {
        if (sourceApp == SourceApp.Discord)
        {
            _settings.DiscordSupportEnabled = enabled;
            UpdateDiscordDetectionVisibility();
        }
        else if (sourceApp == SourceApp.KakaoTalk)
        {
            _settings.KakaoTalkSupportEnabled = enabled;
        }

        MessengerSupportChanged?.Invoke(sourceApp, enabled);
    }

    private async Task ApplyStartupSelection(bool enabled)
    {
        if (StartupChanged is null)
        {
            return;
        }

        foreach (Func<bool, bool, Task> handler in
                 StartupChanged.GetInvocationList())
        {
            await handler(
                _settings.DiscordSupportEnabled,
                enabled);
        }
    }

    private void ApplyThemeSelection(
        SentoryThemeMode mode,
        bool isDark)
    {
        _themeMode = mode;
        _isDarkTheme = isDark;
        _settings.ThemeMode = mode.ToString();
        _settings.IsDarkTheme = isDark;
        ApplyTheme(isDark);
    }

    private void SystemEvents_UserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        if (_themeMode != SentoryThemeMode.System)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var isDark = SentoryThemePreference.ReadWindowsIsDark();
            if (_isDarkTheme == isDark)
            {
                return;
            }

            _isDarkTheme = isDark;
            _settings.IsDarkTheme = isDark;
            ApplyTheme(isDark);
            SaveSettings(SentoryLocalization.Text("ThemeSaveFailed"));
        });
    }

    private void SelectModeButton_Click(
        object sender,
        RoutedEventArgs e) =>
        SetSelectionMode(!_selectionMode);

    private void SelectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button
            {
                Tag: GalleryItemViewModel item
            })
        {
            ToggleSelection(item.Item.ItemId);
        }
    }

    private void SelectVisibleItemsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        foreach (var item in _visibleItems)
        {
            _selectedItemIds.Add(item.Item.ItemId);
        }

        RefreshSelectionState();
    }

    private void ClearSelectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _selectedItemIds.Clear();
        RefreshSelectionState();
    }

    private async void DeleteSelectedButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var selectedItems = _allItems
            .Where(item => _selectedItemIds.Contains(item.Item.ItemId))
            .ToArray();
        if (selectedItems.Length == 0)
        {
            return;
        }

        var favoriteCount = selectedItems.Count(item => item.Item.IsFavorite);
        var favoriteWarning = favoriteCount > 0
            ? SentoryLocalization.Format(
                "SelectedFavoritesWarningFormat",
                favoriteCount)
            : string.Empty;
        if (!SentoryDialogWindow.Confirm(
                this,
                SentoryLocalization.Format(
                    "DeleteSelectedHeadingFormat",
                    selectedItems.Length),
                SentoryLocalization.Text("DeleteSelectedMessage") +
                favoriteWarning +
                SentoryLocalization.Text("CannotUndoLine"),
                SentoryLocalization.Text("DeleteSelected"),
                _isDarkTheme,
                danger: true))
        {
            return;
        }

        DeleteSelectedButton.IsEnabled = false;
        try
        {
            var result = await _repository.DeleteItemsAsync(
                selectedItems.Select(item => item.Item.ItemId).ToArray());
            _allItems.RemoveAll(item =>
                _selectedItemIds.Contains(item.Item.ItemId));
            SetSelectionMode(false);
            ApplyFilter();
            ShowFeedback(
                result.MissingItems == 0
                    ? SentoryLocalization.Format(
                        "DeletedItemsFormat",
                        result.DeletedItems)
                    : SentoryLocalization.Format(
                        "DeletedItemsMissingFormat",
                        result.DeletedItems,
                        result.MissingItems));
        }
        catch (Exception)
        {
            ShowFeedback(SentoryLocalization.Text("DeleteSelectedFailed"));
        }
        finally
        {
            DeleteSelectedButton.IsEnabled = true;
        }
    }

    private void SetSelectionMode(bool enabled)
    {
        CancelSelectionDrag();
        _selectionMode = enabled;
        if (!enabled)
        {
            _selectedItemIds.Clear();
        }

        RefreshSelectionState();
    }

    private void GallerySelectionSurface_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_selectionMode ||
            e.ChangedButton != MouseButton.Left ||
            !GalleryScrollViewer.IsMouseOver ||
            GalleryScrollIndicator.IsMouseOver ||
            IsDragSelectionExcludedSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _selectionDragStart = e.GetPosition(GallerySelectionSurface);
        _selectionDragInProgress = false;
        _selectionDragStartedOnItem =
            FindGalleryItemFromSource(
                e.OriginalSource as DependencyObject) is not null;
        _selectionDragAdditive =
            (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _selectionDragBaseIds.Clear();
        if (_selectionDragAdditive)
        {
            _selectionDragBaseIds.UnionWith(_selectedItemIds);
        }
    }

    private void GallerySelectionSurface_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        UpdateGalleryScrollIndicatorProximity(
            e.GetPosition(GallerySelectionSurface));

        if (!_selectionMode ||
            _selectionDragStart is not Point start ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(GallerySelectionSurface);
        if (!_selectionDragInProgress)
        {
            if (Math.Abs(current.X - start.X) <
                    SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - start.Y) <
                    SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _selectionDragInProgress = true;
            if (!Mouse.Capture(GallerySelectionSurface))
            {
                CancelSelectionDrag();
                return;
            }

            DragSelectionRectangle.Visibility = Visibility.Visible;
        }

        UpdateDragSelectionRectangle(start, current);
        UpdateDragSelectionPreview(CreateSelectionBounds(start, current));
        e.Handled = true;
    }

    private void GallerySelectionSurface_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_selectionDragStart is not Point start)
        {
            return;
        }

        if (!_selectionDragInProgress)
        {
            var clearSelection = !_selectionDragStartedOnItem &&
                                 _selectedItemIds.Count > 0;
            EndSelectionDrag();
            if (clearSelection)
            {
                _selectedItemIds.Clear();
                e.Handled = true;
                RefreshSelectionState();
            }

            return;
        }

        var bounds = CreateSelectionBounds(
            start,
            e.GetPosition(GallerySelectionSurface));
        UpdateDragSelectionRectangle(
            start,
            e.GetPosition(GallerySelectionSurface));
        UpdateDragSelectionPreview(bounds);
        _selectedItemIds.Clear();
        _selectedItemIds.UnionWith(_selectionDragPreviewIds);
        EndSelectionDrag();
        e.Handled = true;
        RefreshSelectionState();
    }

    private void GallerySelectionSurface_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        if (_selectionDragInProgress)
        {
            CancelSelectionDrag();
        }
    }

    private void GallerySelectionSurface_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        if (_scrollIndicatorDragging)
        {
            return;
        }

        SetGalleryScrollIndicatorNear(false);
    }

    private void GalleryScrollViewer_ScrollChanged(
        object sender,
        System.Windows.Controls.ScrollChangedEventArgs e)
    {
        UpdateGalleryScrollIndicator();
        if (Math.Abs(e.VerticalChange) > double.Epsilon)
        {
            ShowGalleryScrollIndicatorAfterScroll();
        }
    }

    private void GalleryScrollIndicator_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        UpdateGalleryScrollIndicator();

    private void GalleryScrollIndicator_MouseEnter(
        object sender,
        MouseEventArgs e)
    {
        SetGalleryScrollIndicatorNear(true);
        SetGalleryScrollIndicatorThumbEmphasis(true);
    }

    private void GalleryScrollIndicator_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        SetGalleryScrollIndicatorThumbEmphasis(
            _scrollIndicatorDragging);
        if (!_scrollIndicatorDragging)
        {
            UpdateGalleryScrollIndicatorProximity(
                Mouse.GetPosition(GallerySelectionSurface));
        }
    }

    private void GalleryScrollIndicator_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            !GalleryScrollIndicator.IsHitTestVisible)
        {
            return;
        }

        _scrollIndicatorDragging = true;
        _scrollIndicatorActive = true;
        _scrollIndicatorHideTimer.Stop();
        SetGalleryScrollIndicatorNear(true);
        SetGalleryScrollIndicatorThumbEmphasis(true);
        GalleryScrollIndicator.CaptureMouse();
        ScrollGalleryToIndicatorPointer(
            e.GetPosition(GalleryScrollIndicator).Y);
        e.Handled = true;
    }

    private void GalleryScrollIndicator_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_scrollIndicatorDragging ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        ScrollGalleryToIndicatorPointer(
            e.GetPosition(GalleryScrollIndicator).Y);
        e.Handled = true;
    }

    private void GalleryScrollIndicator_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_scrollIndicatorDragging ||
            e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        FinishGalleryScrollIndicatorDrag();
        e.Handled = true;
    }

    private void GalleryScrollIndicator_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        if (_scrollIndicatorDragging)
        {
            FinishGalleryScrollIndicatorDrag();
        }
    }

    private void GalleryScrollIndicator_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (!GalleryScrollIndicator.IsHitTestVisible || e.Delta == 0)
        {
            return;
        }

        var wheelLines = SystemParameters.WheelScrollLines;
        if (wheelLines == 0)
        {
            return;
        }

        var notches = Math.Max(
            1,
            Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine);
        if (wheelLines < 0)
        {
            for (var index = 0; index < notches; index++)
            {
                if (e.Delta > 0)
                {
                    GalleryScrollViewer.PageUp();
                }
                else
                {
                    GalleryScrollViewer.PageDown();
                }
            }
        }
        else
        {
            var lineCount = notches * wheelLines;
            for (var index = 0; index < lineCount; index++)
            {
                if (e.Delta > 0)
                {
                    GalleryScrollViewer.LineUp();
                }
                else
                {
                    GalleryScrollViewer.LineDown();
                }
            }
        }

        e.Handled = true;
    }

    private void ScrollIndicatorHideTimer_Tick(
        object? sender,
        EventArgs e)
    {
        _scrollIndicatorHideTimer.Stop();
        _scrollIndicatorActive = false;
        UpdateGalleryScrollIndicatorVisibility();
    }

    private void ShowGalleryScrollIndicatorAfterScroll()
    {
        if (!GalleryScrollIndicator.IsHitTestVisible)
        {
            return;
        }

        _scrollIndicatorActive = true;
        _scrollIndicatorHideTimer.Stop();
        _scrollIndicatorHideTimer.Start();
        UpdateGalleryScrollIndicatorVisibility();
    }

    private void UpdateGalleryScrollIndicator()
    {
        var metrics = ScrollIndicatorMetrics.Calculate(
            GalleryScrollIndicator.ActualHeight,
            GalleryScrollViewer.ExtentHeight,
            GalleryScrollViewer.ViewportHeight,
            GalleryScrollViewer.VerticalOffset);
        GalleryScrollIndicator.IsHitTestVisible = metrics.IsScrollable;
        if (!metrics.IsScrollable)
        {
            _scrollIndicatorHideTimer.Stop();
            _scrollIndicatorNear = false;
            _scrollIndicatorActive = false;
            SetGalleryScrollIndicatorThumbEmphasis(false);
            SetGalleryScrollIndicatorShown(false);
            return;
        }

        GalleryScrollIndicatorThumb.Height = metrics.ThumbHeight;
        GalleryScrollIndicatorThumbTransform.Y = metrics.ThumbTop;
        UpdateGalleryScrollIndicatorVisibility();
    }

    private void UpdateGalleryScrollIndicatorProximity(Point position)
    {
        if (_scrollIndicatorDragging)
        {
            SetGalleryScrollIndicatorNear(true);
            return;
        }

        if (!GalleryScrollIndicator.IsHitTestVisible ||
            GalleryScrollIndicator.ActualHeight <= 0)
        {
            SetGalleryScrollIndicatorNear(false);
            return;
        }

        var topLeft = GalleryScrollIndicator.TranslatePoint(
            new Point(0, 0),
            GallerySelectionSurface);
        var bounds = new Rect(
            topLeft,
            GalleryScrollIndicator.RenderSize);
        var distanceX = Math.Max(
            Math.Max(bounds.Left - position.X, 0),
            position.X - bounds.Right);
        var distanceY = Math.Max(
            Math.Max(bounds.Top - position.Y, 0),
            position.Y - bounds.Bottom);
        var isNear = Math.Sqrt(
            distanceX * distanceX + distanceY * distanceY) <=
            ScrollIndicatorRevealDistance;
        SetGalleryScrollIndicatorNear(isNear);
    }

    private void SetGalleryScrollIndicatorNear(bool isNear)
    {
        if (_scrollIndicatorNear == isNear)
        {
            return;
        }

        _scrollIndicatorNear = isNear;
        UpdateGalleryScrollIndicatorVisibility();
    }

    private void UpdateGalleryScrollIndicatorVisibility() =>
        SetGalleryScrollIndicatorShown(
            GalleryScrollIndicator.IsHitTestVisible &&
            (_scrollIndicatorNear ||
             _scrollIndicatorActive ||
             _scrollIndicatorDragging));

    private void SetGalleryScrollIndicatorShown(bool shown)
    {
        if (_scrollIndicatorShown == shown)
        {
            return;
        }

        _scrollIndicatorShown = shown;
        GalleryScrollIndicator.BeginAnimation(
            OpacityProperty,
            CreateScrollIndicatorAnimation(
                shown ? 1 : 0,
                160));
    }

    private void SetGalleryScrollIndicatorThumbEmphasis(bool emphasized)
    {
        if (_scrollIndicatorThumbEmphasized == emphasized)
        {
            return;
        }

        _scrollIndicatorThumbEmphasized = emphasized;
        GalleryScrollIndicatorThumb.BeginAnimation(
            WidthProperty,
            CreateScrollIndicatorAnimation(
                emphasized ? 6 : 3,
                140));
        GalleryScrollIndicatorThumb.BeginAnimation(
            MarginProperty,
            new ThicknessAnimation
            {
                To = emphasized
                    ? new Thickness(0, 0, 2, 0)
                    : new Thickness(0, 0, 3, 0),
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.HoldEnd
            });
        GalleryScrollIndicatorThumb.BeginAnimation(
            OpacityProperty,
            CreateScrollIndicatorAnimation(
                emphasized ? 0.95 : 0.46,
                140));
    }

    private static DoubleAnimation CreateScrollIndicatorAnimation(
        double target,
        int durationMilliseconds) =>
        new()
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            },
            FillBehavior = FillBehavior.HoldEnd
        };

    private void ScrollGalleryToIndicatorPointer(double pointerY)
    {
        var metrics = ScrollIndicatorMetrics.Calculate(
            GalleryScrollIndicator.ActualHeight,
            GalleryScrollViewer.ExtentHeight,
            GalleryScrollViewer.ViewportHeight,
            GalleryScrollViewer.VerticalOffset);
        if (!metrics.IsScrollable || metrics.ThumbTravel <= 0)
        {
            return;
        }

        var thumbTop = Math.Clamp(
            pointerY - metrics.ThumbHeight / 2,
            0,
            metrics.ThumbTravel);
        var nextOffset = thumbTop / metrics.ThumbTravel *
                         GalleryScrollViewer.ScrollableHeight;
        GalleryScrollViewer.ScrollToVerticalOffset(nextOffset);
    }

    private void FinishGalleryScrollIndicatorDrag()
    {
        _scrollIndicatorDragging = false;
        if (GalleryScrollIndicator.IsMouseCaptured)
        {
            GalleryScrollIndicator.ReleaseMouseCapture();
        }

        SetGalleryScrollIndicatorThumbEmphasis(
            GalleryScrollIndicator.IsMouseOver);
        UpdateGalleryScrollIndicatorProximity(
            Mouse.GetPosition(GallerySelectionSurface));
        UpdateGalleryScrollIndicatorVisibility();
    }

    private void UpdateDragSelectionRectangle(Point start, Point current)
    {
        var bounds = CreateSelectionBounds(start, current);
        System.Windows.Controls.Canvas.SetLeft(
            DragSelectionRectangle,
            bounds.Left);
        System.Windows.Controls.Canvas.SetTop(
            DragSelectionRectangle,
            bounds.Top);
        DragSelectionRectangle.Width = bounds.Width;
        DragSelectionRectangle.Height = bounds.Height;
    }

    private void UpdateDragSelectionPreview(Rect selectionBounds)
    {
        _selectionDragPreviewIds.Clear();
        if (_selectionDragAdditive)
        {
            _selectionDragPreviewIds.UnionWith(_selectionDragBaseIds);
        }

        foreach (var item in _visibleItems)
        {
            if (GalleryItems.ItemContainerGenerator.ContainerFromItem(item)
                    is not FrameworkElement container)
            {
                continue;
            }

            var topLeft = container.TranslatePoint(
                new Point(0, 0),
                GallerySelectionSurface);
            var itemBounds = new Rect(topLeft, container.RenderSize);
            if (selectionBounds.IntersectsWith(itemBounds))
            {
                _selectionDragPreviewIds.Add(item.Item.ItemId);
            }
        }

        ApplyDragSelectionPreviewVisuals();
        SelectedCountText.Text = SentoryLocalization.Format(
            "SelectedCountFormat",
            _selectionDragPreviewIds.Count);
        DeleteSelectedButton.IsEnabled =
            _selectionDragPreviewIds.Count > 0;
    }

    private void ApplyDragSelectionPreviewVisuals()
    {
        foreach (var item in _visibleItems)
        {
            if (GetCardPresenter(item) is not { } presenter)
            {
                continue;
            }

            var selected = _selectionDragPreviewIds.Contains(
                item.Item.ItemId);
            if (FindCardTemplateElement<
                    System.Windows.Controls.Border>(
                    presenter,
                    "CardSelectionOverlay") is { } overlay)
            {
                overlay.Visibility = selected
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (FindCardTemplateElement<
                    System.Windows.Controls.Button>(
                    presenter,
                    "SelectionButton") is { } selectionButton)
            {
                selectionButton.SetResourceReference(
                    System.Windows.Controls.Control.BackgroundProperty,
                    selected ? "AccentBrush" : "SurfaceBrush");
            }

            if (FindCardTemplateElement<
                    System.Windows.Controls.TextBlock>(
                    presenter,
                    "SelectionIconText") is { } selectionIcon)
            {
                selectionIcon.Text = selected
                    ? SelectionCheckGlyph
                    : string.Empty;
                selectionIcon.SetResourceReference(
                    System.Windows.Controls.TextBlock.ForegroundProperty,
                    selected ? "AccentTextBrush" : "AccentBrush");
            }
        }
    }

    private void ClearDragSelectionPreviewVisuals()
    {
        foreach (var item in _visibleItems)
        {
            if (GetCardPresenter(item) is not { } presenter)
            {
                continue;
            }

            if (FindCardTemplateElement<
                    System.Windows.Controls.Border>(
                    presenter,
                    "CardSelectionOverlay") is { } overlay)
            {
                overlay.ClearValue(UIElement.VisibilityProperty);
            }

            if (FindCardTemplateElement<
                    System.Windows.Controls.Button>(
                    presenter,
                    "SelectionButton") is { } selectionButton)
            {
                selectionButton.ClearValue(
                    System.Windows.Controls.Control.BackgroundProperty);
            }

            if (FindCardTemplateElement<
                    System.Windows.Controls.TextBlock>(
                    presenter,
                    "SelectionIconText") is { } selectionIcon)
            {
                selectionIcon.ClearValue(
                    System.Windows.Controls.TextBlock.TextProperty);
                selectionIcon.ClearValue(
                    System.Windows.Controls.TextBlock.ForegroundProperty);
            }
        }
    }

    private System.Windows.Controls.ContentPresenter? GetCardPresenter(
        GalleryItemViewModel item) =>
        GalleryItems.ItemContainerGenerator.ContainerFromItem(item)
            as System.Windows.Controls.ContentPresenter;

    private static T? FindCardTemplateElement<T>(
        System.Windows.Controls.ContentPresenter presenter,
        string elementName)
        where T : FrameworkElement
        => presenter.ContentTemplate?.FindName(elementName, presenter) as T;

    private void CancelSelectionDrag()
    {
        EndSelectionDrag();
    }

    private void EndSelectionDrag()
    {
        ClearDragSelectionPreviewVisuals();
        _selectionDragStart = null;
        _selectionDragInProgress = false;
        _selectionDragAdditive = false;
        _selectionDragStartedOnItem = false;
        _selectionDragBaseIds.Clear();
        _selectionDragPreviewIds.Clear();
        DragSelectionRectangle.Visibility = Visibility.Collapsed;
        DragSelectionRectangle.Width = 0;
        DragSelectionRectangle.Height = 0;
        if (Mouse.Captured == GallerySelectionSurface)
        {
            Mouse.Capture(null);
        }

        if (_selectionMode)
        {
            UpdateSelectionControls();
        }
    }

    private static Rect CreateSelectionBounds(Point start, Point end) =>
        new(
            new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
            new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));

    private static bool IsDragSelectionExcludedSource(
        DependencyObject? source) =>
        FindVisualAncestor<
            System.Windows.Controls.Primitives.ButtonBase>(source) is not null ||
        FindVisualAncestor<
            System.Windows.Controls.Primitives.ScrollBar>(source) is not null;

    private static GalleryItemViewModel? FindGalleryItemFromSource(
        DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement
                {
                    DataContext: GalleryItemViewModel item
                })
            {
                return item;
            }

            current = GetVisualOrLogicalParent(current);
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetVisualOrLogicalParent(current);
        }

        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject current)
        where T : DependencyObject
    {
        if (current is T match)
        {
            return match;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(current);
        for (var index = 0; index < childCount; index++)
        {
            var descendant = FindVisualDescendant<T>(
                VisualTreeHelper.GetChild(current, index));
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static DependencyObject? GetVisualOrLogicalParent(
        DependencyObject current) =>
        current is Visual
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);

    private void ToggleSelection(Guid itemId)
    {
        if (!_selectedItemIds.Add(itemId))
        {
            _selectedItemIds.Remove(itemId);
        }

        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        foreach (var item in _allItems)
        {
            item.SelectionState.Update(
                _selectionMode,
                _selectedItemIds.Contains(item.Item.ItemId));
        }

        UpdateSelectionControls();
    }

    private void UpdateSelectionControls()
    {
        SelectionBar.Visibility = _selectionMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        SelectedCountText.Text = SentoryLocalization.Format(
            "SelectedCountFormat",
            _selectedItemIds.Count);
        DeleteSelectedButton.IsEnabled = _selectedItemIds.Count > 0;
        SelectModeButtonText.Text = SentoryLocalization.Text(
            _selectionMode ? "SelectExit" : "Select");
        SelectModeButton.ToolTip = _selectionMode
            ? SentoryLocalization.Text("SelectExit")
            : SentoryLocalization.Text("Select");
        System.Windows.Automation.AutomationProperties.SetName(
            SelectModeButton,
            SelectModeButton.ToolTip?.ToString() ??
            SentoryLocalization.Text("Select"));
        SearchBox.IsEnabled = !_selectionMode;
        IntegratedFilterButton.IsEnabled = !_selectionMode;
        SortButton.IsEnabled = !_selectionMode;
        DataManagementButton.IsEnabled = !_selectionMode;
        AllFilterButton.IsEnabled = !_selectionMode;
        UrlFilterButton.IsEnabled = !_selectionMode;
        ImageFilterButton.IsEnabled = !_selectionMode;
        FavoriteFilterButton.IsEnabled = !_selectionMode;
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
        SaveSettings(SentoryLocalization.Text("SortSaveFailed"));
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
        var activeFilterCount = IntegratedFilterCountPolicy.Count(
            _sourceApps.Count,
            _dateRange != GalleryDateRange.All);
        FilterCountText.Text = activeFilterCount.ToString();
        FilterCountBadge.Visibility = activeFilterCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        FilterResetButton.IsEnabled = activeFilterCount > 0;
        System.Windows.Automation.AutomationProperties.SetName(
            IntegratedFilterButton,
            activeFilterCount == 0
                ? SentoryLocalization.Text("Filter")
                : SentoryLocalization.Format(
                    "FilterActiveFormat",
                    activeFilterCount));
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
        SourceOptionsPanel.Children.Clear();
        _sourceOptionButtons.Clear();
        _sourceOptionChecks.Clear();
        AddSourceOption(
            "All",
            SentoryLocalization.Text("AllMessengers"));
        foreach (var source in Enum.GetValues<SourceApp>())
        {
            AddSourceOption(source.ToString(), GetSourceLabel(source));
        }
    }

    private void RebuildLocalizedShellControls()
    {
        BuildSourceOptions();
        UpdateSortControls();
        UpdateIntegratedFilterControls();
        DiscordDetectionStatusText.Text =
            DiscordDetectionPresentation.GetLabel(_discordDetectionState);
        SetAvailableUpdate(
            _availableUpdateVersion,
            _updateInstallationInProgress);
        UpdateThemeButtonLabel();
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
        var sortLabel = SentoryLocalization.Text(_sortMode switch
        {
            GallerySortMode.Newest => "SortNewest",
            GallerySortMode.Oldest => "SortOldest",
            GallerySortMode.MostCaptured => "SortMostCaptured",
            GallerySortMode.MostCopied => "SortMostCopied",
            GallerySortMode.RecentlyCopied => "SortRecentlyCopied",
            GallerySortMode.Name => "SortName",
            _ => "SortNewest"
        });
        SortButtonText.Text = SentoryLocalization.Format(
            "SortLabelFormat",
            sortLabel);
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
        SaveSettings(SentoryLocalization.Text("FilterSaveFailed"));
    }

    private static string GetSourceLabel(SourceApp sourceApp) =>
        sourceApp switch
        {
            SourceApp.Discord => "Discord",
            SourceApp.KakaoTalk => SentoryLocalization.Text("KakaoTalk"),
            _ => sourceApp.ToString()
        };

    private async void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        _themeMode = _isDarkTheme
            ? SentoryThemeMode.Dark
            : SentoryThemeMode.Light;
        ApplyTheme(_isDarkTheme);
        _settings.ThemeMode = _themeMode.ToString();
        _settings.IsDarkTheme = _isDarkTheme;
        await Dispatcher.Yield(DispatcherPriority.Background);
        SaveSettings(SentoryLocalization.Text("ThemeSaveFailed"));
    }

    private void ApplyTheme(bool dark)
    {
        SentoryTheme.Apply(Resources, dark);
        SentoryTheme.ApplyDetectionStatus(
            Resources,
            _discordDetectionState,
            dark);

        ThemeIcon.Text = dark ? "\uE706" : "\uE708";
        UpdateThemeButtonLabel();
        ApplyTitleBarTheme();
    }

    private void UpdateThemeButtonLabel()
    {
        var label = _isDarkTheme
            ? SentoryLocalization.Text("SwitchToLight")
            : SentoryLocalization.Text("SwitchToDark");
        ThemeButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(
            ThemeButton,
            label);
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
            _settings.KakaoTalkSupportEnabled =
                current.KakaoTalkSupportEnabled;
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
                ShowFeedback(SentoryLocalization.Text("ItemNotFound"));
                return;
            }

            ReplaceItem(item, item.Item with
            {
                IsFavorite = isFavorite
            });
            ShowFeedback(
                isFavorite
                    ? SentoryLocalization.Text("FavoriteAdded")
                    : SentoryLocalization.Text("FavoriteRemoved"));
        }
        catch (Exception)
        {
            ShowFeedback(SentoryLocalization.Text("FavoriteChangeFailed"));
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

    private async void Card_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border
            {
                DataContext: GalleryItemViewModel item
            })
        {
            if (_selectionMode)
            {
                ToggleSelection(item.Item.ItemId);
                e.Handled = true;
                return;
            }

            await ShowItemDetailsAsync(item);
        }
    }

    private void Artwork_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not System.Windows.Controls.Border
            {
                Tag: GalleryItemViewModel item
            })
        {
            return;
        }

        if (_selectionMode)
        {
            ToggleSelection(item.Item.ItemId);
            return;
        }

        OpenItem(item);
    }

    private void Card_ContextMenuOpening(
        object sender,
        System.Windows.Controls.ContextMenuEventArgs e)
    {
        if (_selectionMode)
        {
            e.Handled = true;
        }
    }

    private async Task ShowItemDetailsAsync(GalleryItemViewModel item)
    {
        var window = new ItemDetailWindow(
            item,
            _isDarkTheme,
            CopyDetailImageAsync,
            LoadDetailLinkArtworkAsync,
            OpenDetailImage,
            OpenDetailLink)
        {
            Owner = this
        };
        var closed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(true);
        window.Show();
        await closed.Task;
        RestoreAfterOwnedWindowClosed();
        switch (window.SelectedAction)
        {
            case ItemDetailAction.Copy:
                await CopyAsync(item);
                break;
            case ItemDetailAction.Open:
                OpenItem(item);
                break;
            case ItemDetailAction.Delete:
                await DeleteAsync(item);
                break;
        }
    }

    private async Task CopyAsync(GalleryItemViewModel item)
    {
        string successMessage;
        try
        {
            if (item.IsCollection)
            {
                var data = CreateCollectionClipboardData(item.Item);
                if (data is null)
                {
                    ShowFeedback(SentoryLocalization.Text("OriginalNotFound"));
                    return;
                }

                await SetClipboardWithRetryAsync(
                    () => WpfClipboard.SetDataObject(data, true));
                successMessage = SentoryLocalization.Text("CollectionCopied");
            }
            else if (item.IsImage)
            {
                var path = ResolveContentPath(item.Item.ContentPath);
                if (path is null || !File.Exists(path))
                {
                    ShowFeedback(SentoryLocalization.Text("PhotoFileNotFound"));
                    return;
                }

                var image = LoadClipboardImage(path);
                await SetClipboardWithRetryAsync(() => WpfClipboard.SetImage(image));
                successMessage = SentoryLocalization.Text("PhotoCopied");
            }
            else
            {
                await SetClipboardWithRetryAsync(
                    () => WpfClipboard.SetText(item.Item.OriginalUrl));
                successMessage = SentoryLocalization.Text("UrlCopied");
            }
        }
        catch (Exception exception)
            when (exception is COMException or ExternalException)
        {
            ShowFeedback(SentoryLocalization.Text("ClipboardBusy"));
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
            ShowFeedback(SentoryLocalization.Text("CopyHistorySaveFailed"));
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

    private async Task<bool> CopyDetailImageAsync(string? contentPath)
    {
        var path = ResolveContentPath(contentPath);
        if (path is null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var image = LoadClipboardImage(path);
            await SetClipboardWithRetryAsync(() => WpfClipboard.SetImage(image));
            return true;
        }
        catch (Exception exception)
            when (exception is COMException or ExternalException or
                  IOException or UnauthorizedAccessException or
                  NotSupportedException)
        {
            return false;
        }
    }

    private void OpenDetailImage(GalleryImageViewModel image) =>
        OpenImageTarget(
            image.ContentPath,
            image.DisplayName,
            image.Sha256);

    private void OpenDetailLink(string url) => OpenTarget(url);

    private void OpenTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            ShowFeedback(SentoryLocalization.Text("OriginalNotFound"));
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
            ShowFeedback(SentoryLocalization.Text("OpenOriginalFailed"));
        }
    }

    private static string GetPhotoName(
        string? contentPath,
        string? displayName = null,
        string? originalFileName = null)
    {
        var preferredTitle = OcrTitleGenerator.CreateBestDisplayTitle(
            originalFileName,
            displayName);
        if (!string.IsNullOrWhiteSpace(preferredTitle))
        {
            return preferredTitle;
        }

        var usefulFileName = OcrTitleGenerator.CreateCandidate(
            Path.GetFileNameWithoutExtension(contentPath));
        return usefulFileName ?? SentoryLocalization.Text("Image");
    }

    private static string CreateOcrSnippet(string text)
    {
        const int maximumLength = 120;
        var normalized = string.Join(
            " · ",
            text.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Take(3));
        return normalized.Length <= maximumLength
            ? normalized
            : $"{normalized[..maximumLength].TrimEnd()}…";
    }

    private async Task<GalleryLinkArtwork?> LoadDetailLinkArtworkAsync(
        DetailLinkViewModel link)
    {
        var cached = _linkPreviewFetcher.FindCachedArtwork(link.NormalizedKey);
        if (cached is null)
        {
            var preview = await _linkPreviewFetcher.FetchAsync(
                new LinkPreviewCandidate(
                    Guid.Empty,
                    link.Url,
                    link.NormalizedKey));
            var relativePath = preview.PreviewImagePath ?? preview.SiteIconPath;
            if (relativePath is null)
            {
                return null;
            }

            cached = new CachedLinkPreviewArtwork(
                relativePath,
                preview.PreviewImagePath is null);
        }

        var image = LoadThumbnail(cached.RelativePath);
        return image is null
            ? null
            : new GalleryLinkArtwork(
                image,
                cached.IsSiteIcon ? Stretch.Uniform : Stretch.UniformToFill,
                cached.IsSiteIcon ? new Thickness(72) : new Thickness(0));
    }

    private System.Windows.DataObject? CreateCollectionClipboardData(
        CapturedItemSummary item)
    {
        var members = item.Members ?? [];
        var urls = members
            .Where(member => member.Kind == ContentKind.Url)
            .Select(member => member.OriginalUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var imagePaths = members
            .Where(member => member.Kind == ContentKind.Image)
            .Select(member => ResolveContentPath(member.ContentPath))
            .Where(path => path is not null && File.Exists(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (urls.Length == 0 && imagePaths.Length == 0)
        {
            return null;
        }

        return CollectionClipboardComposer.Create(urls, imagePaths);
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
        var firstMember = item.Item.Members?.FirstOrDefault();
        if (item.IsCollection && firstMember?.Kind == ContentKind.Image)
        {
            var firstImage = item.CollectionImages.FirstOrDefault(image =>
                string.Equals(
                    image.ContentPath,
                    firstMember.ContentPath,
                    StringComparison.OrdinalIgnoreCase));
            OpenImageTarget(
                firstMember.ContentPath,
                firstImage?.DisplayName ?? GetPhotoName(
                    firstMember.ContentPath,
                    firstMember.OcrDisplayName,
                    firstMember.OriginalUrl),
                firstMember.Sha256);
            return;
        }

        if (item.IsImage)
        {
            OpenImageTarget(
                item.Item.ContentPath,
                item.Title,
                item.Item.Sha256);
            return;
        }

        OpenTarget(item.IsCollection
            ? firstMember?.OriginalUrl
            : item.Item.OriginalUrl);
    }

    private void OpenImageTarget(
        string? contentPath,
        string displayName,
        string? contentIdentity)
    {
        var sourcePath = ResolveContentPath(contentPath);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            ShowFeedback(SentoryLocalization.Text("OriginalNotFound"));
            return;
        }

        var targetPath = sourcePath;
        try
        {
            targetPath = DisplayNamedImageFile.Prepare(
                sourcePath,
                displayName,
                contentIdentity);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  NotSupportedException or ArgumentException)
        {
        }

        OpenTarget(targetPath);
    }

    private async Task DeleteAsync(GalleryItemViewModel item)
    {
        var favoriteWarning = item.Item.IsFavorite
            ? SentoryLocalization.Text("FavoriteDeleteWarning")
            : string.Empty;
        if (!SentoryDialogWindow.Confirm(
                this,
                SentoryLocalization.Text("DeleteItemHeading"),
                SentoryLocalization.Text("DeleteItemMessage") +
                favoriteWarning +
                SentoryLocalization.Text("CannotUndoLine"),
                SentoryLocalization.Text("Delete"),
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
                ShowFeedback(SentoryLocalization.Text("Deleted"));
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            ShowFeedback(SentoryLocalization.Text("DeleteFileFailed"));
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

    private void DismissRuntimeIssueButton_Click(
        object sender,
        RoutedEventArgs e) =>
        SetRuntimeIssue(null);

    private void UpdateAvailableButton_Click(
        object sender,
        RoutedEventArgs e) =>
        UpdateInstallRequested?.Invoke(this, EventArgs.Empty);

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
        var contentVisible = state == ViewState.Content;
        GalleryScrollViewer.Visibility = contentVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        GalleryScrollIndicator.Visibility = contentVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (contentVisible)
        {
            Dispatcher.BeginInvoke(
                UpdateGalleryScrollIndicator,
                DispatcherPriority.Loaded);
        }
        else
        {
            _scrollIndicatorHideTimer.Stop();
            _scrollIndicatorNear = false;
            _scrollIndicatorActive = false;
            SetGalleryScrollIndicatorShown(false);
        }

        LoadingPanel.Visibility =
            state == ViewState.Loading ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility =
            state == ViewState.Empty ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility =
            state == ViewState.Error ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowCloseWithOwnedWindows)
        {
            var visibleOwnedWindow = OwnedWindows
                .Cast<Window>()
                .FirstOrDefault(window => window.IsVisible);
            if (visibleOwnedWindow is not null)
            {
                e.Cancel = true;
                RestoreAfterOwnedWindowClosed();
                visibleOwnedWindow.Activate();
                return;
            }
        }

        SaveWindowPlacement();
        SaveSettings();
        _feedbackCancellation?.Cancel();
        _feedbackCancellation?.Dispose();
        _scrollIndicatorHideTimer.Stop();
        base.OnClosing(e);
    }

    internal void PrepareForApplicationShutdown() =>
        _allowCloseWithOwnedWindows = true;

    private void RestoreAfterOwnedWindowClosed()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (!ShowInTaskbar)
        {
            ShowInTaskbar = true;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            Show();
            Activate();
        }
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
    bool IsCollection,
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
    Thickness ThumbnailMargin,
    string CollectionBadgeText,
    bool HasCollectionBadge,
    IReadOnlyList<GalleryImageViewModel> CollectionImages,
    GalleryItemSelectionState SelectionState) : INotifyPropertyChanged
{
    private string _title = Title;
    private string _subtitle = Subtitle;
    private string _typeLabel = TypeLabel;
    private string _dateLabel = DateLabel;
    private string _statusLabel = StatusLabel;
    private string _initial = Initial;
    private string _collectionBadgeText = CollectionBadgeText;

    public string Title => _title;

    public string Subtitle => _subtitle;

    public string TypeLabel => _typeLabel;

    public string DateLabel => _dateLabel;

    public string StatusLabel => _statusLabel;

    public string Initial => _initial;

    public string CollectionBadgeText => _collectionBadgeText;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Domain => IsCollection
        ? Item.Members?.FirstOrDefault(member =>
              member.Kind == ContentKind.Url)?.Domain is { Length: > 0 } domain
            ? domain
            : SentoryLocalization.Format(
                "CollectionItemsFormat",
                Item.Members?.Count ?? 0)
        : Item.Domain;

    public string FavoriteIcon =>
        Item.IsFavorite ? "\uE735" : "\uE734";

    public string FavoriteToolTip =>
        Item.IsFavorite
            ? SentoryLocalization.Text("FavoriteRemove")
            : SentoryLocalization.Text("FavoriteAdd");

    public string FavoriteMenuLabel => FavoriteToolTip;

    public bool HasBeenCopied => Item.CopyCount > 0;

    public string CopyUsageLabel => SentoryLocalization.Format(
        "CopyUsageFormat",
        Item.CopyCount);

    public string AutomationName =>
        SentoryLocalization.Format(
            "ItemAutomationFormat",
            TypeLabel,
            Title,
            DateLabel,
            Item.CaptureCount,
            Item.CopyCount);

    internal void ApplyLocalizedText(GalleryItemLocalizedText text)
    {
        SetLocalizedProperty(ref _title, text.Title, nameof(Title));
        SetLocalizedProperty(
            ref _subtitle,
            text.Subtitle,
            nameof(Subtitle));
        SetLocalizedProperty(
            ref _typeLabel,
            text.TypeLabel,
            nameof(TypeLabel));
        SetLocalizedProperty(
            ref _dateLabel,
            text.DateLabel,
            nameof(DateLabel));
        SetLocalizedProperty(
            ref _statusLabel,
            text.StatusLabel,
            nameof(StatusLabel));
        SetLocalizedProperty(ref _initial, text.Initial, nameof(Initial));
        SetLocalizedProperty(
            ref _collectionBadgeText,
            text.CollectionBadgeText,
            nameof(CollectionBadgeText));
        OnPropertyChanged(nameof(Domain));
        OnPropertyChanged(nameof(FavoriteToolTip));
        OnPropertyChanged(nameof(FavoriteMenuLabel));
        OnPropertyChanged(nameof(CopyUsageLabel));
        OnPropertyChanged(nameof(AutomationName));
    }

    private void SetLocalizedProperty(
        ref string property,
        string value,
        string propertyName)
    {
        if (string.Equals(property, value, StringComparison.Ordinal))
        {
            return;
        }

        property = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

internal sealed record GalleryItemLocalizedText(
    string Title,
    string Subtitle,
    string TypeLabel,
    string DateLabel,
    string StatusLabel,
    string Initial,
    string CollectionBadgeText);

public sealed class GalleryItemSelectionState : INotifyPropertyChanged
{
    private bool _isSelectionMode;
    private bool _isSelected;

    public GalleryItemSelectionState(bool isSelectionMode, bool isSelected)
    {
        _isSelectionMode = isSelectionMode;
        _isSelected = isSelected;
    }

    public bool IsSelectionMode => _isSelectionMode;

    public bool IsSelected => _isSelected;

    public string SelectionIcon => _isSelected ? "\uE73E" : string.Empty;

    public string SelectionToolTip => SentoryLocalization.Text(
        _isSelected ? "DeselectItem" : "SelectItem");

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Update(bool isSelectionMode, bool isSelected)
    {
        if (_isSelectionMode != isSelectionMode)
        {
            _isSelectionMode = isSelectionMode;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsSelectionMode)));
        }

        if (_isSelected == isSelected)
        {
            return;
        }

        _isSelected = isSelected;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(IsSelected)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(SelectionIcon)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(SelectionToolTip)));
    }
}

public sealed record GalleryImageViewModel(
    string? ContentPath,
    ImageSource? Thumbnail,
    string DisplayName,
    string? Sha256);

public sealed record GalleryLinkArtwork(
    ImageSource Image,
    Stretch Stretch,
    Thickness Margin);
