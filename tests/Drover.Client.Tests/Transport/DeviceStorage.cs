namespace Drover.Transport;

// Test stand-ins for the platform storage types used by the linked transport source.
public interface ISecureStorage
{
#pragma warning disable DRV0001 // Matches the storage API used by the linked transport.
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);
#pragma warning restore DRV0001

    bool Remove(string key);
}

public sealed class Preferences
{
    #region Properties

    public static Preferences Default { get; } = new();

    #endregion

    #region Public Methods

    public string Get(string key, string fallback) => fallback;

    public void Set(string key, string value)
    {
    }

    public void Remove(string key)
    {
    }

    #endregion
}
