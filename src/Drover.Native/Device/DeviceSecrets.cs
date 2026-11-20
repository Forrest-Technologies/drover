#if __ANDROID__ || __IOS__
namespace Drover.Native.Platform;

/// <summary>
/// Where the sign-in that outlives the app is kept on a phone: the platform's credential store, which the device's
/// hardware backs. A desktop keeps it in the operating system's credential store instead (<c>DesktopSecrets</c>).
/// </summary>
internal sealed class DeviceSecrets(ILogger<DeviceSecrets> logger) : ISecureStorage
{
    #region Private Fields

    private const string Resource = "Drover";

#if __ANDROID__
    private readonly Windows.Security.Credentials.PasswordVault vault = new();
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

#if __IOS__
    private string? Read(string key)
    {
        using var query = Query(key);
        using var data = Security.SecKeyChain.QueryAsData(query, wantPersistentReference: false, out var status);
        if (status == Security.SecStatusCode.ItemNotFound || data is null)
        {
            logger.LogDebug("No saved Drover session was found in the credential store");
            return null;
        }

        if (status != Security.SecStatusCode.Success)
        {
            throw new InvalidOperationException($"The iOS keychain could not read the Drover session ({status}).");
        }

        return System.Text.Encoding.UTF8.GetString([.. data]);
    }

    private static void Write(string key, string value)
    {
        using var query = Query(key);
        using var data = Foundation.NSData.FromArray(System.Text.Encoding.UTF8.GetBytes(value));
        using var attributes = new Security.SecRecord
        {
            Accessible = Security.SecAccessible.AfterFirstUnlockThisDeviceOnly,
            ValueData = data,
        };

        var status = Security.SecKeyChain.Update(query, attributes);
        if (status == Security.SecStatusCode.ItemNotFound)
        {
            using var entry = Query(key);
            entry.Accessible = Security.SecAccessible.AfterFirstUnlockThisDeviceOnly;
            entry.ValueData = data;
            status = Security.SecKeyChain.Add(entry);
        }

        if (status != Security.SecStatusCode.Success)
        {
            throw new InvalidOperationException($"The iOS keychain could not save the Drover session ({status}).");
        }
    }

    private static bool Forget(string key)
    {
        using var query = Query(key);
        var status = Security.SecKeyChain.Remove(query);
        if (status == Security.SecStatusCode.ItemNotFound)
        {
            return false;
        }

        if (status != Security.SecStatusCode.Success)
        {
            throw new InvalidOperationException($"The iOS keychain could not remove the Drover session ({status}).");
        }

        return true;
    }

    private static Security.SecRecord Query(string key) => new(Security.SecKind.GenericPassword)
    {
        Service = Resource,
        Account = key,
    };
#else
    private string? Read(string key)
    {
        if (Find(key) is not { } kept)
        {
            logger.LogDebug("No saved Drover session was found in the credential store");
            return null;
        }

        kept.RetrievePassword();
        return kept.Password;
    }

    private void Write(string key, string value) => vault.Add(new(Resource, key, value));

    private bool Forget(string key)
    {
        if (Find(key) is not { } kept)
        {
            return false;
        }

        vault.Remove(kept);
        return true;
    }

    private Windows.Security.Credentials.PasswordCredential? Find(string key) => vault.RetrieveAll().FirstOrDefault(
        credential => string.Equals(credential.Resource, Resource, StringComparison.OrdinalIgnoreCase)
            && string.Equals(credential.UserName, key, StringComparison.OrdinalIgnoreCase));
#endif

    #endregion
}
#endif
