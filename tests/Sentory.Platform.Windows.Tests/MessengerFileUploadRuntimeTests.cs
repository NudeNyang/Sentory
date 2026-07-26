using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class MessengerFileUploadRuntimeTests
{
    [Fact]
    public void Resolve_AcceptsRootedSupportedImageAndRejectsOtherFiles()
    {
        var expected = Path.GetFullPath(@"C:\Images\photo.png");

        var result = FileDialogPathResolver.Resolve(
            [expected, @"C:\Images\notes.txt"],
            [],
            path => string.Equals(
                path,
                expected,
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal([expected], result);
    }

    [Fact]
    public void Resolve_CombinesQuotedMultipleSelectionWithCurrentFolder()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            $"sentory-file-dialog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var first = Path.Combine(folder, "first.png");
            var second = Path.Combine(folder, "second.jpg");
            var expected = new HashSet<string>(
                [first, second],
                StringComparer.OrdinalIgnoreCase);

            var result = FileDialogPathResolver.Resolve(
                ["\"first.png\" \"second.jpg\""],
                [$"Address: {folder}"],
                expected.Contains);

            Assert.Equal(2, result.Count);
            Assert.All(result, path => Assert.Contains(path, expected));
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void Resolve_DeduplicatesFilenameAndSelectedItemValues()
    {
        var expected = Path.GetFullPath(@"C:\Images\same.webp");

        var result = FileDialogPathResolver.Resolve(
            [expected, expected, $"\"{expected}\""],
            [],
            path => string.Equals(
                path,
                expected,
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal([expected], result);
    }

    [Fact]
    public void Resolve_FindsActualImageWhenExplorerHidesItsExtension()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            $"sentory-hidden-extension-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var expected = Path.Combine(folder, "photo.png");
            File.WriteAllBytes(expected, [1]);

            var result = FileDialogPathResolver.Resolve(
                ["photo"],
                [$"Address: {folder}"]);

            Assert.Equal([expected], result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void DecisionTracker_LastOpenOrCancelDecisionWinsAndIsConsumed()
    {
        var tracker = new FileDialogDecisionTracker();
        var dialog = new nint(1234);

        tracker.Track(dialog);
        tracker.Record(dialog, FileDialogDecision.Accepted);
        tracker.Record(dialog, FileDialogDecision.Cancelled);

        Assert.Equal(FileDialogDecision.Cancelled, tracker.Take(dialog));
        Assert.Equal(FileDialogDecision.Unknown, tracker.Take(dialog));
    }

    [Fact]
    public void DecisionTracker_IgnoresDialogsThatAreNotTracked()
    {
        var tracker = new FileDialogDecisionTracker();
        var dialog = new nint(1234);

        tracker.Record(dialog, FileDialogDecision.Accepted);
        Assert.Equal(FileDialogDecision.Unknown, tracker.Take(dialog));

        tracker.Track(dialog);
        tracker.Record(dialog, FileDialogDecision.Accepted);
        tracker.Untrack(dialog);
        Assert.Equal(FileDialogDecision.Unknown, tracker.Take(dialog));
    }

    [Fact]
    public void DiscordManualUpload_RequiresObservedDraftBeforeMessageConfirmation()
    {
        Assert.False(DiscordManualUploadConfirmationPolicy.CanConfirm(
            trackDraft: true,
            observedDraft: false,
            matchingOwnedImageFound: true));
        Assert.True(DiscordManualUploadConfirmationPolicy.CanConfirm(
            trackDraft: true,
            observedDraft: true,
            matchingOwnedImageFound: true));
    }

    [Fact]
    public void DiscordManualUpload_CancelsRemovedDraftAfterGracePeriod()
    {
        var missingSince = DateTimeOffset.UtcNow;

        Assert.False(DiscordManualUploadConfirmationPolicy.ShouldCancel(
            trackDraft: true,
            observedDraft: true,
            draftImageCount: 0,
            missingSince,
            missingSince.AddSeconds(1)));
        Assert.True(DiscordManualUploadConfirmationPolicy.ShouldCancel(
            trackDraft: true,
            observedDraft: true,
            draftImageCount: 0,
            missingSince,
            missingSince.AddSeconds(2)));
    }

    [Fact]
    public void DiscordManualUpload_CancelsWhenDraftNeverAppears()
    {
        var startedAt = DateTimeOffset.UtcNow;

        Assert.False(
            DiscordManualUploadConfirmationPolicy
                .ShouldCancelUnobservedDraft(
                    trackDraft: true,
                    observedDraft: false,
                    startedAt,
                    startedAt.AddSeconds(4)));
        Assert.True(
            DiscordManualUploadConfirmationPolicy
                .ShouldCancelUnobservedDraft(
                    trackDraft: true,
                    observedDraft: false,
                    startedAt,
                    startedAt.AddSeconds(5)));
    }

    [Theory]
    [InlineData(0x0D, 0, (int)FileDialogDecision.Accepted)]
    [InlineData(0x1B, 0, (int)FileDialogDecision.Cancelled)]
    [InlineData(0x0D, 2, (int)FileDialogDecision.Cancelled)]
    [InlineData(0x41, 0, (int)FileDialogDecision.Unknown)]
    public void FileDialogInput_ClassifiesKeyboardDecision(
        int virtualKey,
        int focusedControlId,
        int expected)
    {
        Assert.Equal(
            (FileDialogDecision)expected,
            FileDialogInputPolicy.ClassifyKeyboard(
                virtualKey,
                focusedControlId));
    }

    [Theory]
    [InlineData(1, (int)FileDialogDecision.Accepted)]
    [InlineData(2, (int)FileDialogDecision.Cancelled)]
    [InlineData(1148, (int)FileDialogDecision.Unknown)]
    public void FileDialogInput_ClassifiesMouseControl(
        int controlId,
        int expected)
    {
        Assert.Equal(
            (FileDialogDecision)expected,
            FileDialogInputPolicy.ClassifyControl(controlId));
    }
}
