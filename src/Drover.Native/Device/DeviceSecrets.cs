namespace Drover.Native.Platform;

/// <summary>
/// Where the sign-in that outlives the app is kept. On a phone that is the platform's credential store, which the
/// device's hardware backs. A desktop has no such store the app can use, so there nothing is written anywhere:
/// the sign-in lasts until the app closes, and the person signs in again next time.
/// </summary>
internal sealed class DeviceSecrets(ILogger<DeviceSecrets> logger) : ISecureStorage
{
    #region Private Fields

#if __ANDROID__ || __IOS__
    private const string Resource = "Drover";

    private readonly Windows.Security.Credentials.PasswordVault vault = new();
#else
    private readonly Dictionary<string, string> untilClosed = [];
#endif

    #endregion

    #region Interface Implementations

    // The names are the ones the shared session code calls.
#pragma warning disable DRV0001
    public Task<string?> GetAsync(string key) => Task.FromResult(Read(key));

    public Task SetAsync(string key, string value)
    {
        Write(key, value);
        return Task.CompletedTask;
    }
#pragma warning restore DRV0001

    public bool Remove(string key) => Forget(key);

    #endregion

    #region Private Methods

#if __ANDROID__ || __IOS__
    private string? Read(string key)
    {
        try
        {
            var kept = vault.Retrieve(Resource, key);
            kept.RetrievePassword();
            return kept.Password;
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            logger.LogDebug(exception, "Nothing is kept in the credential store under that name");
            return null;
        }
    }

    private void Write(string key, string value) => vault.Add(new(Resource, key, value));

    private bool Forget(string key)
    {
        try
        {
            vault.Remove(vault.Retrieve(Resource, key));
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            logger.LogDebug(exception, "Nothing was kept in the credential store under that name");
            return false;
        }
    }
#else
    private string? Read(string key)
    {
        logger.LogDebug("This platform has no credential store, so the sign-in is kept only while the app is open");
        return untilClosed.GetValueOrDefault(key);
    }

    private void Write(string key, string value) => untilClosed[key] = value;

    private bool Forget(string key) => untilClosed.Remove(key);
#endif

    #endregion
}
