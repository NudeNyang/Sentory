using Sentory.Core;

namespace Sentory.App;

internal enum CopyUsageRecordOutcome
{
    Recorded,
    AutoFavoriteAdded,
    RecordFailed,
    AutoFavoriteFailed
}

internal sealed record CopyUsageRecordResult(
    CopyUsageRecordOutcome Outcome,
    CapturedItemSummary Item);

internal sealed class CopyUsageRecorder(ICaptureRepository repository)
{
    public async Task<CopyUsageRecordResult> RecordAsync(
        CapturedItemSummary item,
        bool autoFavoriteEnabled,
        int autoFavoriteCopyThreshold,
        DateTimeOffset copiedAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await repository.RecordCopyAsync(
                    item.ItemId,
                    copiedAt,
                    cancellationToken))
            {
                return new CopyUsageRecordResult(
                    CopyUsageRecordOutcome.RecordFailed,
                    item);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new CopyUsageRecordResult(
                CopyUsageRecordOutcome.RecordFailed,
                item);
        }

        var updatedItem = item with
        {
            CopyCount = item.CopyCount + 1,
            LastCopiedAt = copiedAt
        };
        if (!AutoFavoritePolicy.ShouldAdd(
                updatedItem.Kind,
                updatedItem.IsFavorite,
                updatedItem.CopyCount,
                autoFavoriteEnabled,
                autoFavoriteCopyThreshold))
        {
            return new CopyUsageRecordResult(
                CopyUsageRecordOutcome.Recorded,
                updatedItem);
        }

        try
        {
            if (!await repository.SetFavoriteAsync(
                    updatedItem.ItemId,
                    true,
                    cancellationToken))
            {
                return new CopyUsageRecordResult(
                    CopyUsageRecordOutcome.AutoFavoriteFailed,
                    updatedItem);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new CopyUsageRecordResult(
                CopyUsageRecordOutcome.AutoFavoriteFailed,
                updatedItem);
        }

        return new CopyUsageRecordResult(
            CopyUsageRecordOutcome.AutoFavoriteAdded,
            updatedItem with
            {
                IsFavorite = true
            });
    }
}
