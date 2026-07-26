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
    public void DecisionTracker_AccumulatesSingleAndMultipleSelectionEvents()
    {
        var tracker = new FileDialogDecisionTracker();
        var dialog = new nint(1234);
        var observedAt = DateTimeOffset.UtcNow;
        tracker.Track(dialog);

        tracker.RecordSelection(
            dialog,
            FileDialogSelectionChange.Replace,
            ["first"],
            observedAt);
        tracker.RecordSelection(
            dialog,
            FileDialogSelectionChange.Add,
            ["second"],
            observedAt.AddMilliseconds(10));

        var snapshot = tracker.TakeSnapshot(dialog);

        Assert.Equal(FileDialogDecision.Unknown, snapshot.Decision);
        Assert.Equal(["first", "second"], snapshot.RawSelections);
        Assert.Equal(observedAt.AddMilliseconds(10), snapshot.SelectedAt);
    }

    [Fact]
    public void DecisionTracker_RemovesDeselectedItemAndImplicitTimestamp()
    {
        var tracker = new FileDialogDecisionTracker();
        var dialog = new nint(1234);
        var observedAt = DateTimeOffset.UtcNow;
        tracker.Track(dialog);
        tracker.RecordSelection(
            dialog,
            FileDialogSelectionChange.Replace,
            ["photo"],
            observedAt);

        tracker.RecordSelection(
            dialog,
            FileDialogSelectionChange.Remove,
            ["photo"],
            observedAt.AddMilliseconds(10));
        var snapshot = tracker.TakeSnapshot(dialog);

        Assert.Empty(snapshot.RawSelections);
        Assert.Null(snapshot.SelectedAt);
    }

    [Fact]
    public void FileDialogCompletion_AcceptsOnlyRecentImplicitSelection()
    {
        var selectedAt = DateTimeOffset.UtcNow;

        Assert.Equal(
            FileDialogDecision.Accepted,
            FileDialogCompletionPolicy.Resolve(
                FileDialogDecision.Unknown,
                selectedPathCount: 1,
                selectedAt,
                selectedAt.AddMilliseconds(400)));
        Assert.Equal(
            FileDialogDecision.Unknown,
            FileDialogCompletionPolicy.Resolve(
                FileDialogDecision.Unknown,
                selectedPathCount: 1,
                selectedAt,
                selectedAt.AddSeconds(2)));
        Assert.Equal(
            FileDialogDecision.Unknown,
            FileDialogCompletionPolicy.Resolve(
                FileDialogDecision.Unknown,
                selectedPathCount: 1,
                selectedAt.AddSeconds(1),
                selectedAt));
        Assert.Equal(
            FileDialogDecision.Cancelled,
            FileDialogCompletionPolicy.Resolve(
                FileDialogDecision.Cancelled,
                selectedPathCount: 1,
                selectedAt,
                selectedAt.AddMilliseconds(100)));
    }

    [Theory]
    [InlineData(0x8006, (int)FileDialogSelectionChange.Replace)]
    [InlineData(0x8007, (int)FileDialogSelectionChange.Add)]
    [InlineData(0x8008, (int)FileDialogSelectionChange.Remove)]
    [InlineData(0x800E, (int)FileDialogSelectionChange.Replace)]
    public void AccessibilityEvent_MapsSelectionChanges(
        uint eventType,
        int expected)
    {
        Assert.Equal(
            (FileDialogSelectionChange)expected,
            FileDialogAccessibilityEventPolicy.MapSelectionChange(eventType));
    }

    [Fact]
    public void AccessibilityEvent_IgnoresUnrelatedEvents()
    {
        Assert.Null(
            FileDialogAccessibilityEventPolicy.MapSelectionChange(0x800A));
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
