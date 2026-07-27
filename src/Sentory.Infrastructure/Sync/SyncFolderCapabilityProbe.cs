using System.Security.Cryptography;

namespace Sentory.Infrastructure.Sync;

public enum SyncFolderCapabilityFailure
{
    NotDirectory,
    ReadWriteUnavailable,
    RenameUnavailable,
    ContentMismatch
}

public sealed record SyncFolderCapabilityResult(
    bool IsSupported,
    SyncFolderCapabilityFailure? FailureReason = null);

public static class SyncFolderCapabilityProbe
{
    public static async Task<SyncFolderCapabilityResult> CheckAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var fullPath = Path.GetFullPath(folderPath);
        if (File.Exists(fullPath))
        {
            return new SyncFolderCapabilityResult(
                false,
                SyncFolderCapabilityFailure.NotDirectory);
        }

        var probeDirectory = Path.Combine(
            fullPath,
            $".sentory-probe-{Guid.NewGuid():N}");
        var source = Path.Combine(probeDirectory, "한글-source.tmp");
        var destination = Path.Combine(probeDirectory, "한글-renamed.tmp");
        try
        {
            Directory.CreateDirectory(probeDirectory);
            var expected = RandomNumberGenerator.GetBytes(64);
            await File.WriteAllBytesAsync(source, expected, cancellationToken);
            try
            {
                File.Move(source, destination, overwrite: false);
            }
            catch (Exception exception)
                when (exception is IOException or
                      UnauthorizedAccessException or
                      NotSupportedException)
            {
                return new SyncFolderCapabilityResult(
                    false,
                    SyncFolderCapabilityFailure.RenameUnavailable);
            }

            var actual = await File.ReadAllBytesAsync(
                destination,
                cancellationToken);
            return expected.AsSpan().SequenceEqual(actual)
                ? new SyncFolderCapabilityResult(true)
                : new SyncFolderCapabilityResult(
                    false,
                    SyncFolderCapabilityFailure.ContentMismatch);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  NotSupportedException)
        {
            return new SyncFolderCapabilityResult(
                false,
                SyncFolderCapabilityFailure.ReadWriteUnavailable);
        }
        finally
        {
            try
            {
                if (Directory.Exists(probeDirectory))
                {
                    Directory.Delete(probeDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
