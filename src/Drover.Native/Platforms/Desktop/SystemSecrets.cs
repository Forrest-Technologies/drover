namespace Drover.Native.Platform;

/// <summary>
/// The operating system's own credential store: the login Keychain on macOS, the Secret Service (through libsecret)
/// on Linux, and on Windows a file under the person's local application data that only their Windows account can
/// decrypt (DPAPI). Each key is its own entry. <see cref="Open"/> writes and reads back a probe first, so a computer
/// without a working store is found before a sign-in is trusted to it.
/// </summary>
internal sealed class SystemSecrets : ISecureStorage
{
    #region Private Fields

    private const string Service = "Drover";

    private const string Schema = "com.forresttechnologies.drover.secret";

    private const string Probe = "drover.native.store-check";

    private static readonly string windowsFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drover", "Secrets");

    #endregion

    #region Public Methods

    /// <summary>The store, after a probe was written to it and read back. Throws when the computer has none.</summary>
    public static ISecureStorage Open()
    {
        Entry(Probe).VerifyPersistence();
        return new SystemSecrets();
    }

    #endregion

    #region Interface Implementations

    // The names are the ones the shared session code calls.
#pragma warning disable DRV0001
    public Task<string?> GetAsync(string key) =>
        Task.FromResult(Entry(key).ReadData() is { Length: > 0 } kept ? Encoding.UTF8.GetString(kept) : null);

    public Task SetAsync(string key, string value)
    {
        Entry(key).WriteData(Encoding.UTF8.GetBytes(value));
        return Task.CompletedTask;
    }
#pragma warning restore DRV0001

    public bool Remove(string key)
    {
        var entry = Entry(key);
        var existed = entry.ReadData().Length > 0;
        entry.Clear();
        return existed;
    }

    #endregion

    #region Private Methods

    private static Storage Entry(string key) => Storage.Create(
        new StorageCreationPropertiesBuilder(FileName(key), windowsFolder)
            .WithMacKeyChain(Service, key)
            .WithLinuxKeyring(
                Schema,
                MsalCacheHelper.LinuxKeyRingDefaultCollection,
                $"{Service} sign-in",
                new("application", Service),
                new("key", key))
            .Build());

    // A key names a server address, which a Windows file name cannot hold.
    private static string FileName(string key) =>
        $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))}.bin";

    #endregion
}
