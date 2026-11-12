namespace Drover.Adapters.DataProtection;

public static class SecretVaultExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the vault and keeps the key ring on its own path, apart from the database, so a database dump
    /// alone does not reveal a secret. Losing the key ring loses every stored secret.
    /// </summary>
    public static IServiceCollection AddDroverSecretVault(this IServiceCollection services, string keyRingPath)
    {
        services.AddDataProtection()
            .SetApplicationName("Drover")
            .PersistKeysToFileSystem(CreateKeyRingDirectory(keyRingPath));
        services.AddScoped<ISecretVault, DataProtectionSecretVault>();
        services.AddHostedService(provider => new KeyRingPermissionsGuard(
            keyRingPath,
            provider.GetRequiredService<ILogger<KeyRingPermissionsGuard>>()));
        return services;
    }

    #endregion

    #region Private Methods

    private static DirectoryInfo CreateKeyRingDirectory(string path)
    {
        var directory = new DirectoryInfo(path);

        if (directory.Exists)
        {
            return directory;
        }

        if (OperatingSystem.IsWindows())
        {
            directory.Create();
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return directory;
    }

    #endregion
}
