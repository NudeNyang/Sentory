using System.Windows;
using System.Windows.Media;
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

    public ItemDetailWindow(
        GalleryItemViewModel item,
        bool isDarkTheme)
    {
        InitializeComponent();
        _isDarkTheme = isDarkTheme;
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
        OpenButton.Content = SentoryLocalization.Text(
            item.IsImage ? "OpenPhoto" : "OpenLink");
        CopyButton.Content = SentoryLocalization.Text(
            item.IsImage ? "CopyPhoto" : "CopyUrl");

        if (item.Thumbnail is not null)
        {
            ArtworkImage.Source = item.Thumbnail;
            ArtworkImage.Stretch = item.IsImage
                ? Stretch.Uniform
                : Stretch.UniformToFill;
            ArtworkImage.Visibility = Visibility.Visible;
            ArtworkFallback.Visibility = Visibility.Collapsed;
        }

        SourceInitialized += (_, _) =>
            SentoryTheme.ApplyTitleBar(this, _isDarkTheme);
    }

    public ItemDetailAction SelectedAction { get; private set; }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ItemDetailAction.Copy);

    private void OpenButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ItemDetailAction.Open);

    private void DeleteButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ItemDetailAction.Delete);

    private void Complete(ItemDetailAction action)
    {
        SelectedAction = action;
        Close();
    }
}
