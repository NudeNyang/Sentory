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
    private readonly IReadOnlyList<ImageSource> _collectionImages;
    private int _collectionImageIndex;

    public ItemDetailWindow(
        GalleryItemViewModel item,
        bool isDarkTheme)
    {
        InitializeComponent();
        _isDarkTheme = isDarkTheme;
        _collectionImages = item.CollectionImages;
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
            var links = item.Item.Members?
                .Where(member => member.Kind == ContentKind.Url)
                .Select(member => new DetailLinkViewModel(member.OriginalUrl))
                .ToArray() ?? [];
            if (links.Length > 0)
            {
                CollectionLinksList.ItemsSource = links;
                CollectionLinksSection.Visibility = Visibility.Visible;
            }
        }
        else if (!item.IsImage && !string.IsNullOrWhiteSpace(item.Item.OriginalUrl))
        {
            OriginalBorder.Visibility = Visibility.Collapsed;
            CollectionLinksList.ItemsSource =
                new[] { new DetailLinkViewModel(item.Item.OriginalUrl) };
            CollectionLinksSection.Visibility = Visibility.Visible;
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

        if (_collectionImages.Count > 0)
        {
            ShowCollectionImage(0, animate: false);
            if (_collectionImages.Count > 1)
            {
                ArtworkImage.Margin = new Thickness(22, 20, 22, 60);
                PhotoNavigation.Visibility = Visibility.Visible;
                StackBackOne.Visibility = Visibility.Visible;
                StackBackTwo.Visibility = _collectionImages.Count > 2
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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
        if (_collectionImages.Count == 0)
        {
            return;
        }

        _collectionImageIndex =
            (requestedIndex % _collectionImages.Count + _collectionImages.Count) %
            _collectionImages.Count;
        ArtworkImageBrush.ImageSource = _collectionImages[_collectionImageIndex];
        ArtworkImageBrush.Stretch = Stretch.Uniform;
        ArtworkImage.Visibility = Visibility.Visible;
        ArtworkFallback.Visibility = Visibility.Collapsed;
        PhotoPositionText.Text = SentoryLocalization.Format(
            "PhotoPositionFormat",
            _collectionImageIndex + 1,
            _collectionImages.Count);

        if (_collectionImages.Count > 1)
        {
            StackBackOneBrush.ImageSource = _collectionImages[
                (_collectionImageIndex + 1) % _collectionImages.Count];
        }

        if (_collectionImages.Count > 2)
        {
            StackBackTwoBrush.ImageSource = _collectionImages[
                (_collectionImageIndex + 2) % _collectionImages.Count];
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

    private void Complete(ItemDetailAction action)
    {
        SelectedAction = action;
        Close();
    }
}

public sealed record DetailLinkViewModel(string Url);
