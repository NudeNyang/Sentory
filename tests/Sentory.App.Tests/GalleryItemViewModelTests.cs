using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Sentory.App.Tests;

public sealed class GalleryItemViewModelTests
{
    [Fact]
    public void MissingCardThumbnailDoesNotBlockTheBindingGetter()
    {
        using var loadStarted = new ManualResetEventSlim();
        using var allowLoad = new ManualResetEventSlim();
        ImageSource expected = new DrawingImage();
        var reference = new GalleryArtworkReference(
            () =>
            {
                loadStarted.Set();
                allowLoad.Wait();
                return expected;
            },
            preferBackgroundLoad: true);
        var viewModel = new GalleryItemViewModel(
            null!,
            true,
            false,
            "title",
            "subtitle",
            "type",
            "date",
            "status",
            "T",
            reference,
            null,
            null,
            true,
            false,
            Stretch.Uniform,
            new Thickness(8),
            string.Empty,
            false,
            [],
            new GalleryItemSelectionState(false, false));

        Assert.Null(viewModel.Thumbnail);
        Assert.True(loadStarted.Wait(TimeSpan.FromSeconds(2)));
        allowLoad.Set();
        Assert.True(SpinWait.SpinUntil(
            () => reference.IsValueCreated,
            TimeSpan.FromSeconds(2)));
        Assert.Same(expected, viewModel.Thumbnail);
    }

    [Fact]
    public void AppliesLocalizedTextWithoutReplacingArtworkOrSelectionState()
    {
        var selectionState = new GalleryItemSelectionState(false, false);
        var loads = 0;
        ImageSource thumbnail = new DrawingImage();
        var thumbnailReference = new GalleryArtworkReference(() =>
        {
            loads++;
            return thumbnail;
        });
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
            thumbnailReference,
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
        Assert.Equal(0, loads);
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
        Assert.Equal(1, loads);
        Assert.Same(selectionState, viewModel.SelectionState);
        Assert.Contains(nameof(GalleryItemViewModel.Title), changedProperties);
        Assert.Contains(
            nameof(GalleryItemViewModel.AutomationName),
            changedProperties);
    }
}
