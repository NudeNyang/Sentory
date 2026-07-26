namespace Sentory.Core.Sync;

public interface IReadableSyncObjectStore : ISyncObjectStore
{
    string CreateImageObjectKey(
        string sha256,
        string fileExtension);
}
