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
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
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
    private readonly HashSet<Guid> _selectedItemIds = [];
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
    private bool _selectionMode;
    private Point? _selectionDragStart;
    private bool _selectionDragInProgress;
    private bool _selectionDragAdditive;
    private bool _selectionDragStartedOnItem;
    private readonly HashSet<Guid> _selectionDragBaseIds = [];
    private readonly HashSet<Guid> _selectionDragPreviewIds = [];
    private bool _discordRepairNeeded;
    private CaptureRuntimeState _discordDetectionState =
        CaptureRuntimeState.Connecting;

    public event EventHandler? DiscordRepairRequested;

    public event EventHandler? DiscordSupportChanged;

    public event EventHandler? LanguageChanged;

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
        _discordRepairNeeded = needed;
        DiscordConnectionBanner.Visibility = needed
            ? Visibility.Visible
            : Visibility.Collapsed;
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

    private GalleryItemViewModel CreateViewModel(CapturedItemSummary item)
    {
        var isImage = item.Kind == ContentKind.Image;
        var title = isImage
            ? SentoryLocalization.Text("ClipboardImage")
            : !string.IsNullOrWhiteSpace(item.PageTitle)
                ? item.PageTitle
            : string.IsNullOrWhiteSpace(item.Domain)
                ? SentoryLocalization.Text("SavedLink")
                : item.Domain;
        var subtitle = isImage
            ? SentoryLocalization.Text("PngImage")
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
            $"{SentoryLocalization.Text(isImage ? "Image" : "Link")} · " +
            GetSourceLabel(item.LastSourceApp),
            SentoryLocalization.FormatDate(item.LastCapturedAt.LocalDateTime),
            item.DeliveryStatus == DeliveryStatus.NotObserved
                ? SentoryLocalization.Text("SavedOnInput")
                : item.LastSourceApp == SourceApp.Discord
                    ? SentoryLocalization.Text("DiscordSent")
                    : SentoryLocalization.Text("SentConfirmed"),
            GetInitial(title),
            thumbnail,
            siteIcon,
            thumbnail is not null,
            siteIcon is not null,
            isImage ? Stretch.Uniform : Stretch.UniformToFill,
            isImage ? new Thickness(8) : new Thickness(0),
            _selectionMode,
            _selectedItemIds.Contains(item.ItemId));
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

        UpdateSelectionControls();

        if (_allItems.Count > 0 && _visibleItems.Count == 0)
        {
            EmptyTitleText.Text = SentoryLocalization.Text("NoSearchResults");
            EmptyDescriptionText.Text = SentoryLocalization.Text(
                "NoSearchResultsDescription");
        }
        else
        {
            EmptyTitleText.Text = SentoryLocalization.Text("NoItems");
            EmptyDescriptionText.Text =
                SentoryLocalization.Text("NoItemsDescription");
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
            _paths,
            _discordDetectionState,
            _discordRepairNeeded,
            _isDarkTheme)
        {
            Owner = this
        };
        window.ShowDialog();
        if (window.HasDataChanged)
        {
            await RefreshAsync();
        }

        if (window.ThemeChanged)
        {
            _isDarkTheme = _settingsStore.Load().IsDarkTheme;
            _settings.IsDarkTheme = _isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }

        if (window.LanguageChanged)
        {
            _settings.Language = _settingsStore.Load().Language;
            RebuildLocalizedControls();
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        if (window.DiscordSupportChanged)
        {
            DiscordSupportChanged?.Invoke(this, EventArgs.Empty);
        }

        if (window.DiscordRepairRequested)
        {
            DiscordRepairRequested?.Invoke(this, EventArgs.Empty);
        }
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

        RebuildItemViewModels();
    }

    private void ClearSelectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _selectedItemIds.Clear();
        RebuildItemViewModels();
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

        RebuildItemViewModels();
    }

    private void GallerySelectionSurface_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_selectionMode ||
            e.ChangedButton != MouseButton.Left ||
            !GalleryScrollViewer.IsMouseOver ||
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
                RebuildItemViewModels();
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
        RebuildItemViewModels();
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
            if (GetCardTemplateElement<
                    System.Windows.Controls.Border>(
                    item,
                    "CardSelectionOverlay") is not { } overlay)
            {
                continue;
            }

            var selected = _selectionDragPreviewIds.Contains(
                item.Item.ItemId);
            overlay.Visibility = selected
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void ClearDragSelectionPreviewVisuals()
    {
        foreach (var item in _visibleItems)
        {
            if (GetCardTemplateElement<
                    System.Windows.Controls.Border>(
                    item,
                    "CardSelectionOverlay") is not { } overlay)
            {
                continue;
            }

            overlay.ClearValue(UIElement.VisibilityProperty);
        }
    }

    private T? GetCardTemplateElement<T>(
        GalleryItemViewModel item,
        string elementName)
        where T : FrameworkElement
    {
        if (GalleryItems.ItemContainerGenerator.ContainerFromItem(item)
                is not System.Windows.Controls.ContentPresenter presenter)
        {
            return null;
        }

        return presenter.ContentTemplate?.FindName(elementName, presenter)
            as T;
    }

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

        RebuildItemViewModels();
    }

    private void RebuildItemViewModels()
    {
        for (var index = 0; index < _allItems.Count; index++)
        {
            _allItems[index] = CreateViewModel(_allItems[index].Item);
        }

        ApplyFilter();
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

    private void RebuildLocalizedControls()
    {
        BuildSourceOptions();
        UpdateSortControls();
        UpdateIntegratedFilterControls();
        SetDiscordDetectionState(_discordDetectionState);
        ApplyTheme(_isDarkTheme);
        RebuildItemViewModels();
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

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme(_isDarkTheme);
        _settings.IsDarkTheme = _isDarkTheme;
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
        var label = dark
            ? SentoryLocalization.Text("SwitchToLight")
            : SentoryLocalization.Text("SwitchToDark");
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
        var window = new ItemDetailWindow(item, _isDarkTheme)
        {
            Owner = this
        };
        window.ShowDialog();
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
            if (item.IsImage)
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
    Thickness ThumbnailMargin,
    bool IsSelectionMode,
    bool IsSelected)
{
    public string Domain => Item.Domain;

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

    public string SelectionIcon => IsSelected ? "\uE73E" : string.Empty;

    public string SelectionToolTip => SentoryLocalization.Text(
        IsSelected ? "DeselectItem" : "SelectItem");

    public string AutomationName =>
        SentoryLocalization.Format(
            "ItemAutomationFormat",
            TypeLabel,
            Title,
            DateLabel,
            Item.CaptureCount,
            Item.CopyCount);
}
