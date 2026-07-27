using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sentory.Core;
using Sentory.Infrastructure.Ocr;

namespace Sentory.App;

public enum ItemDetailAction
{
    None,
    Copy,
    Open,
    Delete
}

public partial class ItemDetailWindow : Window
{
    private readonly bool _isDarkTheme;
    private readonly IReadOnlyList<GalleryImageViewModel> _detailImages;
    private readonly IReadOnlyList<DetailLinkViewModel> _detailLinks;
    private readonly Func<string?, Task<DetailImageCopyResult>>
        _copyImageAsync;
    private readonly Func<DetailLinkViewModel, Task<GalleryLinkArtwork?>>
        _loadLinkArtworkAsync;
    private readonly Action<GalleryImageViewModel> _openImage;
    private readonly Action<string> _openLink;
    private readonly OverlayScrollIndicatorController _scrollIndicator;
    private readonly Dictionary<string, GalleryLinkArtwork?> _linkArtworkCache =
        new(StringComparer.OrdinalIgnoreCase);
    private int _collectionImageIndex;
    private int _linkIndex;
    private int _linkArtworkGeneration;
    private bool _artworkDisplaysLink;
    private System.Windows.Point? _artworkPointerDown;
    private bool _artworkPointerDragged;

    public ItemDetailWindow(
        GalleryItemViewModel item,
        bool isDarkTheme,
        Func<string?, Task<DetailImageCopyResult>> copyImageAsync,
        Func<DetailLinkViewModel, Task<GalleryLinkArtwork?>>
            loadLinkArtworkAsync,
        Action<GalleryImageViewModel> openImage,
        Action<string> openLink)
    {
        InitializeComponent();
        _isDarkTheme = isDarkTheme;
        _copyImageAsync = copyImageAsync;
        _loadLinkArtworkAsync = loadLinkArtworkAsync;
        _openImage = openImage;
        _openLink = openLink;
        _detailImages = item.IsCollection
            ? item.CollectionImages
            : item.IsImage && item.Thumbnail is not null
                ? [new GalleryImageViewModel(
                    item.Item.ContentPath,
                    item.Thumbnail,
                    item.Title,
                    item.Item.Sha256)]
                : [];
        _detailLinks = item.IsCollection
            ? item.Item.Members?
                .Where(member => member.Kind == ContentKind.Url)
                .Select(member => new DetailLinkViewModel(
                    member.OriginalUrl,
                    member.NormalizedKey,
                    member.Domain))
                .ToArray() ?? []
            : !item.IsImage && !string.IsNullOrWhiteSpace(item.Item.OriginalUrl)
                ? [new DetailLinkViewModel(
                    item.Item.OriginalUrl,
                    item.Item.NormalizedKey,
                    item.Item.Domain)]
                : [];
        SentoryTheme.Apply(Resources, isDarkTheme);
        _scrollIndicator = new OverlayScrollIndicatorController(
            DetailScrollViewer,
            DetailScrollSurface,
            DetailScrollIndicator,
            DetailScrollIndicatorThumb,
            DetailScrollIndicatorThumbTransform);
        TypeText.Text = item.TypeLabel;
        TitleText.Text = item.Title;
        DescriptionText.Text = item.Subtitle;
        FavoriteText.Visibility = item.Item.IsFavorite
            ? Visibility.Visible
            : Visibility.Collapsed;
        InitialText.Text = item.Initial;
        DomainText.Text = item.IsImage
            ? SentoryLocalization.Text("StoredPhoto")
            : item.Domain;
        if (_detailLinks.Count > 0)
        {
            CollectionLinksSection.Visibility = Visibility.Visible;
            SetLinkSelection(0);
            LinkNavigation.Visibility = _detailLinks.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (_detailImages.Count == 0)
            {
                _ = ShowLinkAsync(0);
            }
        }
        CaptureCountText.Text = SentoryLocalization.Format(
            "TimesFormat",
            item.Item.CaptureCount);
        CopyCountText.Text = SentoryLocalization.Format(
            "TimesFormat",
            item.Item.CopyCount);
        SourceText.Text = item.Item.LastSourceApp switch
        {
            SourceApp.Discord => "Discord",
            SourceApp.KakaoTalk => SentoryLocalization.Text("KakaoTalk"),
            SourceApp.Slack => "Slack",
            SourceApp.WhatsApp => "WhatsApp",
            _ => item.Item.LastSourceApp.ToString()
        };
        SavedAtText.Text = item.Item.LastCapturedAt.LocalDateTime
            .ToString("yyyy. M. d. HH:mm");
        DeliveryText.Text = item.StatusLabel;
        if (item.Thumbnail is not null)
        {
            ArtworkImageBrush.ImageSource = item.Thumbnail;
            ArtworkImageBrush.Stretch = item.ThumbnailStretch;
            ArtworkImage.Visibility = Visibility.Visible;
            ArtworkFallback.Visibility = Visibility.Collapsed;
        }

        if (_detailImages.Count > 0)
        {
            ShowCollectionImage(0, animate: false);
            PhotoControlsSection.Visibility = Visibility.Visible;
            PhotoNavigation.Visibility = _detailImages.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        SourceInitialized += (_, _) =>
            SentoryTheme.ApplyTitleBar(this, _isDarkTheme);
        SelectableTextBlock.EnableExtendedHitTesting(this);
        OwnedPopupDismissBehavior.Enable(this);
        Closed += (_, _) => _scrollIndicator.Dispose();
    }

    public ItemDetailAction SelectedAction { get; private set; }

    private void CopyButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ItemDetailAction.Copy);

    private void OpenButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ItemDetailAction.Open);

    private void DeleteButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ItemDetailAction.Delete);

    private void PreviousPhotoButton_Click(object sender, RoutedEventArgs e) =>
        ShowCollectionImage(_collectionImageIndex - 1, animate: true);

    private void NextPhotoButton_Click(object sender, RoutedEventArgs e) =>
        ShowCollectionImage(_collectionImageIndex + 1, animate: true);

    private async void PreviousLinkButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await ShowLinkAsync(_linkIndex - 1);

    private async void NextLinkButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await ShowLinkAsync(_linkIndex + 1);

    private async void CopyCurrentPhotoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_detailImages.Count == 0)
        {
            return;
        }

        var selectedImage = _detailImages[_collectionImageIndex];
        CurrentPhotoCopyButton.IsEnabled = false;
        var result = await _copyImageAsync(selectedImage.ContentPath);
        if (result.CopyCount is { } copyCount)
        {
            CopyCountText.Text = SentoryLocalization.Format(
                "TimesFormat",
                copyCount);
        }

        if (result.IsFavorite is { } isFavorite)
        {
            FavoriteText.Visibility = isFavorite
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        CurrentPhotoCopyIcon.Text =
            result.Copied ? "\uE73E" : "\uE783";
        CurrentPhotoCopyButton.ToolTip = SentoryLocalization.Text(
            result.Copied ? "Copied" : "CopyFailedShort");
        await Task.Delay(1000);
        if (CurrentPhotoCopyButton.IsLoaded)
        {
            CurrentPhotoCopyIcon.Text = "\uE8C8";
            CurrentPhotoCopyButton.ToolTip = SentoryLocalization.Text(
                "CopyCurrentPhoto");
            CurrentPhotoCopyButton.IsEnabled = true;
        }
    }

    private void ArtworkSurface_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _artworkPointerDown = e.GetPosition(ArtworkSurface);
        _artworkPointerDragged = false;
        Mouse.Capture(ArtworkSurface, CaptureMode.Element);
        e.Handled = true;
    }

    private void ArtworkSurface_MouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (_artworkPointerDown is not { } origin ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(ArtworkSurface);
        if (Math.Abs(current.X - origin.X) >=
                SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - origin.Y) >=
                SystemParameters.MinimumVerticalDragDistance)
        {
            _artworkPointerDragged = true;
        }
    }

    private void ArtworkSurface_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        var shouldOpen = _artworkPointerDown is not null &&
            !_artworkPointerDragged;
        ResetArtworkPointerGesture();
        if (Mouse.Captured == ArtworkSurface)
        {
            Mouse.Capture(null);
        }

        if (shouldOpen)
        {
            OpenCurrentArtwork();
        }

        e.Handled = true;
    }

    private void ArtworkSurface_LostMouseCapture(
        object sender,
        System.Windows.Input.MouseEventArgs e) =>
        ResetArtworkPointerGesture();

    private void ResetArtworkPointerGesture()
    {
        _artworkPointerDown = null;
        _artworkPointerDragged = false;
    }

    private void ArtworkSurface_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        OpenCurrentArtwork();
        e.Handled = true;
    }

    private void OpenCurrentArtwork()
    {
        if (_artworkDisplaysLink && _detailLinks.Count > 0)
        {
            _openLink(_detailLinks[_linkIndex].Url);
            return;
        }

        if (_detailImages.Count > 0)
        {
            _openImage(_detailImages[_collectionImageIndex]);
        }
    }

    private async void CopyCollectionLinkButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string url } button ||
            string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(url);
            SetCopyButtonFeedback(button, "\uE73E", "Copied");
            await Task.Delay(1000);
            if (button.IsLoaded)
            {
                SetCopyButtonFeedback(button, "\uE8C8", "CopyUrl");
            }
        }
        catch (Exception exception)
            when (exception is System.Runtime.InteropServices.ExternalException or
                  InvalidOperationException)
        {
            SetCopyButtonFeedback(button, "\uE783", "CopyFailedShort");
        }
    }

    private static void SetCopyButtonFeedback(
        System.Windows.Controls.Button button,
        string glyph,
        string tooltipKey)
    {
        if (button.Content is System.Windows.Controls.TextBlock icon)
        {
            icon.Text = glyph;
        }

        button.ToolTip = SentoryLocalization.Text(tooltipKey);
    }

    private void ShowCollectionImage(int requestedIndex, bool animate)
    {
        if (_detailImages.Count == 0)
        {
            return;
        }

        _linkArtworkGeneration++;
        _collectionImageIndex =
            (requestedIndex % _detailImages.Count + _detailImages.Count) %
            _detailImages.Count;
        _artworkDisplaysLink = false;
        var selectedImage = _detailImages[_collectionImageIndex];
        ArtworkImageBrush.ImageSource =
            selectedImage.Thumbnail;
        ArtworkImageBrush.Stretch = Stretch.Uniform;
        ArtworkImage.Margin = new Thickness(12);
        ArtworkImage.BorderThickness = new Thickness(0);
        ArtworkImage.Visibility = Visibility.Visible;
        ArtworkFallback.Visibility = Visibility.Collapsed;
        CurrentPhotoNameText.Text = selectedImage.DisplayName;
        CurrentPhotoNameText.ToolTip = selectedImage.DisplayName;
        ArtworkSurface.ToolTip = SentoryLocalization.Text("OpenPhoto");
        StackBackOne.Visibility = _detailImages.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        StackBackTwo.Visibility = _detailImages.Count > 2
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdatePageDots(
            PhotoPageDots,
            _detailImages.Count,
            _collectionImageIndex);

        if (_detailImages.Count > 1)
        {
            StackBackOneBrush.ImageSource = _detailImages[
                (_collectionImageIndex + 1) % _detailImages.Count].Thumbnail;
        }

        if (_detailImages.Count > 2)
        {
            StackBackTwoBrush.ImageSource = _detailImages[
                (_collectionImageIndex + 2) % _detailImages.Count].Thumbnail;
        }

        if (animate)
        {
            ArtworkImage.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });
        }
    }

    private void SetLinkSelection(int requestedIndex)
    {
        if (_detailLinks.Count == 0)
        {
            return;
        }

        _linkIndex =
            (requestedIndex % _detailLinks.Count + _detailLinks.Count) %
            _detailLinks.Count;
        CollectionLinksList.ItemsSource =
            new[] { _detailLinks[_linkIndex] };
        UpdatePageDots(LinkPageDots, _detailLinks.Count, _linkIndex);
    }

    private async Task ShowLinkAsync(int requestedIndex)
    {
        SetLinkSelection(requestedIndex);
        var link = _detailLinks[_linkIndex];
        _artworkDisplaysLink = true;
        ArtworkSurface.ToolTip = SentoryLocalization.Text("OpenLink");
        var generation = ++_linkArtworkGeneration;
        ShowLinkFallback(link);

        if (!_linkArtworkCache.TryGetValue(
                link.NormalizedKey,
                out var artwork))
        {
            artwork = await _loadLinkArtworkAsync(link);
            _linkArtworkCache[link.NormalizedKey] = artwork;
        }

        if (generation != _linkArtworkGeneration || artwork is null)
        {
            return;
        }

        ArtworkImageBrush.ImageSource = artwork.Image;
        ArtworkImageBrush.Stretch = artwork.Stretch;
        ArtworkImage.Margin = artwork.Margin;
        ArtworkImage.BorderThickness = new Thickness(0);
        ArtworkImage.Visibility = Visibility.Visible;
        ArtworkFallback.Visibility = Visibility.Collapsed;
    }

    private void ShowLinkFallback(DetailLinkViewModel link)
    {
        StackBackOne.Visibility = Visibility.Collapsed;
        StackBackTwo.Visibility = Visibility.Collapsed;
        ArtworkImage.Visibility = Visibility.Collapsed;
        ArtworkFallback.Visibility = Visibility.Visible;
        InitialText.Text = string.IsNullOrWhiteSpace(link.Domain)
            ? "L"
            : link.Domain[..1].ToUpperInvariant();
        DomainText.Text = link.Domain;
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
            System.IO.Path.GetFileNameWithoutExtension(contentPath));
        return usefulFileName ?? SentoryLocalization.Text("Image");
    }

    private void UpdatePageDots(
        System.Windows.Controls.StackPanel panel,
        int count,
        int selectedIndex)
    {
        panel.Children.Clear();
        var accent = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var muted = (System.Windows.Media.Brush)FindResource("LineBrush");
        for (var index = 0; index < count; index++)
        {
            panel.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = index == selectedIndex ? 9 : 5,
                Height = 5,
                Margin = new Thickness(2, 0, 2, 0),
                Fill = index == selectedIndex ? accent : muted,
                Opacity = index == selectedIndex ? 0.9 : 0.72
            });
        }
    }

    private void Complete(ItemDetailAction action)
    {
        SelectedAction = action;
        Close();
    }
}

public sealed record DetailLinkViewModel(
    string Url,
    string NormalizedKey,
    string Domain);

public sealed record DetailImageCopyResult(
    bool Copied,
    int? CopyCount = null,
    bool? IsFavorite = null);
