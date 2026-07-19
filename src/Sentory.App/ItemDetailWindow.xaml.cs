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
        DomainText.Text = item.IsImage ? "저장된 사진" : item.Domain;
        OriginalText.Text = item.IsImage
            ? item.Item.ContentPath ?? "사진 파일 경로를 찾지 못했습니다."
            : item.Item.OriginalUrl;
        CaptureCountText.Text = $"{item.Item.CaptureCount:N0}회";
        CopyCountText.Text = $"{item.Item.CopyCount:N0}회";
        SourceText.Text = item.Item.LastSourceApp == SourceApp.Discord
            ? "Discord"
            : "카카오톡";
        SavedAtText.Text = item.Item.LastCapturedAt.LocalDateTime
            .ToString("yyyy. M. d. HH:mm");
        DeliveryText.Text = item.StatusLabel;
        OpenButton.Content = item.IsImage ? "사진 열기" : "링크 열기";
        CopyButton.Content = item.IsImage ? "사진 복사" : "URL 복사";

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
