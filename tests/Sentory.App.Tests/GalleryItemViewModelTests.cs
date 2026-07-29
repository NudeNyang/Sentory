using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Sentory.App.Tests;

public sealed class GalleryItemViewModelTests
{
    [Fact]
    public void AppliesLocalizedTextWithoutReplacingArtworkOrSelectionState()
    {
        var selectionState = new GalleryItemSelectionState(false, false);
        ImageSource? thumbnail = null;
        var viewModel = new GalleryItemViewModel(
            null!,
            false,
            false,
            "old title",
            "old subtitle",
            "old type",
            "old date",
            "old status",
            "O",
            thumbnail,
            null,
            null,
            false,
            false,
            Stretch.Uniform,
            new Thickness(0),
            "old badge",
            false,
            [],
            selectionState);
        var changedProperties = new List<string?>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged +=
            (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.ApplyLocalizedText(new GalleryItemLocalizedText(
            "new title",
            "new subtitle",
            "new type",
            "new date",
            "new status",
            "N",
            "new badge"));

        Assert.Equal("new title", viewModel.Title);
        Assert.Equal("new subtitle", viewModel.Subtitle);
        Assert.Equal("new type", viewModel.TypeLabel);
        Assert.Equal("new date", viewModel.DateLabel);
        Assert.Equal("new status", viewModel.StatusLabel);
        Assert.Equal("N", viewModel.Initial);
        Assert.Equal("new badge", viewModel.CollectionBadgeText);
        Assert.Same(thumbnail, viewModel.Thumbnail);
        Assert.Same(selectionState, viewModel.SelectionState);
        Assert.Contains(nameof(GalleryItemViewModel.Title), changedProperties);
        Assert.Contains(
            nameof(GalleryItemViewModel.AutomationName),
            changedProperties);
    }
}
