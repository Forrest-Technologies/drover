namespace Drover.Native.Platform;

// The app's store interface (src/Drover.Native/Device/StorageShims.cs), which sits beside Uno types this project
// does not reference.
public interface ISecureStorage
{
#pragma warning disable DRV0001 // Matches the storage API used by the linked source.
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);
#pragma warning restore DRV0001

    bool Remove(string key);
}
