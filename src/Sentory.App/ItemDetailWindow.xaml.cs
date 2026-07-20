using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sentory.Core;

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
    private readonly Func<string?, Task<bool>> _copyImageAsync;
    private int _collectionImageIndex;
    private int _linkIndex;

    public ItemDetailWindow(
        GalleryItemViewModel item,
        bool isDarkTheme,
        Func<string?, Task<bool>> copyImageAsync)
    {
        InitializeComponent();
        _isDarkTheme = isDarkTheme;
        _copyImageAsync = copyImageAsync;
        _detailImages = item.IsCollection
            ? item.CollectionImages
            : item.IsImage && item.Thumbnail is not null
                ? [new GalleryImageViewModel(
                    item.Item.ContentPath,
                    item.Thumbnail)]
                : [];
        _detailLinks = item.IsCollection
            ? item.Item.Members?
                .Where(member => member.Kind == ContentKind.Url)
                .Select(member => new DetailLinkViewModel(member.OriginalUrl))
                .ToArray() ?? []
            : !item.IsImage && !string.IsNullOrWhiteSpace(item.Item.OriginalUrl)
                ? [new DetailLinkViewModel(item.Item.OriginalUrl)]
                : [];
        SentoryTheme.Apply(Resources, isDarkTheme);
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
        OriginalText.Text = item.IsImage
            ? item.Item.ContentPath ??
              SentoryLocalization.Text("MissingPhotoPath")
            : item.Item.OriginalUrl;
        if (item.IsCollection)
        {
            OriginalBorder.Visibility = Visibility.Collapsed;
        }
        else if (!item.IsImage && !string.IsNullOrWhiteSpace(item.Item.OriginalUrl))
        {
            OriginalBorder.Visibility = Visibility.Collapsed;
        }
        if (_detailLinks.Count > 0)
        {
            CollectionLinksSection.Visibility = Visibility.Visible;
            ShowLink(0);
            LinkNavigation.Visibility = _detailLinks.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        CaptureCountText.Text = SentoryLocalization.Format(
            "TimesFormat",
            item.Item.CaptureCount);
        CopyCountText.Text = SentoryLocalization.Format(
            "TimesFormat",
            item.Item.CopyCount);
        SourceText.Text = item.Item.LastSourceApp == SourceApp.Discord
            ? "Discord"
            : SentoryLocalization.Text("KakaoTalk");
        SavedAtText.Text = item.Item.LastCapturedAt.LocalDateTime
            .ToString("yyyy. M. d. HH:mm");
        DeliveryText.Text = item.StatusLabel;
        var opensImage = item.IsCollection
            ? item.Item.Members?.FirstOrDefault()?.Kind == ContentKind.Image
            : item.IsImage;
        OpenButton.Content = SentoryLocalization.Text(
            opensImage ? "OpenPhoto" : "OpenLink");
        CopyButton.Content = SentoryLocalization.Text(
            item.IsCollection
                ? "CopyCollection"
                : item.IsImage ? "CopyPhoto" : "CopyUrl");

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
            ArtworkImage.Margin = new Thickness(18, 18, 18, 58);
            PhotoNavigation.Visibility = Visibility.Visible;
            if (_detailImages.Count > 1)
            {
                ArtworkImage.Margin = new Thickness(22, 20, 22, 60);
                ArtworkImage.BorderThickness = new Thickness(2);
                StackBackOne.Visibility = Visibility.Visible;
                StackBackTwo.Visibility = _detailImages.Count > 2
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else
            {
                PreviousPhotoButton.Visibility = Visibility.Collapsed;
                NextPhotoButton.Visibility = Visibility.Collapsed;
            }
        }

        SourceInitialized += (_, _) =>
            SentoryTheme.ApplyTitleBar(this, _isDarkTheme);
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

    private void PreviousLinkButton_Click(object sender, RoutedEventArgs e) =>
        ShowLink(_linkIndex - 1);

    private void NextLinkButton_Click(object sender, RoutedEventArgs e) =>
        ShowLink(_linkIndex + 1);

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
        var copied = await _copyImageAsync(selectedImage.ContentPath);
        CurrentPhotoCopyIcon.Text = copied ? "\uE73E" : "\uE783";
        CurrentPhotoCopyButton.ToolTip = SentoryLocalization.Text(
            copied ? "Copied" : "CopyFailedShort");
        await Task.Delay(1000);
        if (CurrentPhotoCopyButton.IsLoaded)
        {
            CurrentPhotoCopyIcon.Text = "\uE8C8";
            CurrentPhotoCopyButton.ToolTip = SentoryLocalization.Text(
                "CopyCurrentPhoto");
            CurrentPhotoCopyButton.IsEnabled = true;
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
            button.Content = SentoryLocalization.Text("Copied");
            await Task.Delay(1000);
            if (button.IsLoaded)
            {
                button.Content = SentoryLocalization.Text("Copy");
            }
        }
        catch (Exception exception)
            when (exception is System.Runtime.InteropServices.ExternalException or
                  InvalidOperationException)
        {
            button.Content = SentoryLocalization.Text("CopyFailedShort");
        }
    }

    private void ShowCollectionImage(int requestedIndex, bool animate)
    {
        if (_detailImages.Count == 0)
        {
            return;
        }

        _collectionImageIndex =
            (requestedIndex % _detailImages.Count + _detailImages.Count) %
            _detailImages.Count;
        ArtworkImageBrush.ImageSource =
            _detailImages[_collectionImageIndex].Thumbnail;
        ArtworkImageBrush.Stretch = Stretch.Uniform;
        ArtworkImage.Visibility = Visibility.Visible;
        ArtworkFallback.Visibility = Visibility.Collapsed;
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

    private void ShowLink(int requestedIndex)
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

public sealed record DetailLinkViewModel(string Url);
