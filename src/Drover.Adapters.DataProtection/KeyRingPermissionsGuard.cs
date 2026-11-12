namespace Drover.Adapters.DataProtection;

/// <summary>
/// At start, on Unix, makes the key ring readable by its owner only: the directory 0700 and the key files 0600. A key
/// ring created by an earlier version, a backup restore, or an operator may be open to the group or to everyone.
/// If the permissions cannot be changed it logs a warning and the server still starts.
/// </summary>
public sealed class KeyRingPermissionsGuard(string keyRingPath, ILogger<KeyRingPermissionsGuard> logger) : IHostedService
{
    #region Private Fields

    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    #endregion

    #region Interface Implementations

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() && Directory.Exists(keyRingPath))
        {
            Tighten();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion

    #region Private Methods

    [UnsupportedOSPlatform("windows")]
    private void Tighten()
    {
        try
        {
            SetModeIfDifferent(keyRingPath, DirectoryMode);

            foreach (var file in Directory.EnumerateFiles(keyRingPath))
            {
                SetModeIfDifferent(file, FileMode);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "The key ring at {KeyRingPath} is open to other users and its permissions could not be tightened.",
                keyRingPath);
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void SetModeIfDifferent(string path, UnixFileMode mode)
    {
        if (File.GetUnixFileMode(path) != mode)
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    #endregion
}
