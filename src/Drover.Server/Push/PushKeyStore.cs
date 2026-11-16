namespace Drover.Server.Push;

/// <summary>
/// The server's Web Push key pair. It belongs to the whole server, not to a space: a browser holds one subscription
/// per site, made with this public key, whatever space its person works in. So it is not a space secret in the vault.
/// It is made at first start, encrypted with the key ring's Data Protection keys, and kept beside the key ring, so the
/// same backup holds both and a database dump alone never reveals it. A file that exists is never replaced: a new pair
/// would silently cut off every device that subscribed with the old one. One that cannot be read turns push off and
/// says why; it is never made again behind the operator's back.
/// </summary>
public sealed class PushKeyStore(string keyRingPath, IDataProtectionProvider protection, ILogger<PushKeyStore> logger)
{
    #region Private Fields

    public const string FileName = "web-push-keys";

    private const string Purpose = "Drover.Push.WebPushKeys.v1";

    #endregion

    #region Properties

    public string FilePath => Path.Combine(keyRingPath, FileName);

    #endregion

    #region Public Methods

    /// <summary>
    /// The stored pair, or a new one stored now; or, when the stored pair cannot be read, push off with the reason.
    /// <paramref name="subject"/> is the contact push services see.
    /// </summary>
    public PushAvailability Open(string subject)
    {
        var protector = protection.CreateProtector(Purpose);

        if (!File.Exists(FilePath))
        {
            TryCreate(protector, subject);
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredKeys>(protector.Unprotect(File.ReadAllText(FilePath)))
                ?? throw new InvalidDataException($"{FileName} is empty.");
            return PushAvailability.On(new WebPushKeys { PublicKey = stored.PublicKey, PrivateKey = stored.PrivateKey, Subject = subject });
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException or IOException)
        {
            logger.LogError(exception, "The server's Web Push keys at {Path} cannot be read; notifications are off", FilePath);
            return PushAvailability.Off(
                $"Notifications are off: {FilePath} cannot be read with the current key ring. Restore the key ring it was made with, or delete the file to make a new pair, after which every device has to turn notifications on again.");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Writes a new pair to a file of its own, then moves it into place only if no pair is there. When another server
    /// process got there first, its pair stays and is the one used, so two servers never hold different pairs.
    /// </summary>
    private void TryCreate(IDataProtector protector, string subject)
    {
        var keys = WebPushKeys.Generate(subject);
        var draft = $"{FilePath}.{Guid.NewGuid():N}.new";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var writer = new StreamWriter(new FileStream(draft, options)))
        {
            writer.Write(protector.Protect(JsonSerializer.Serialize(new StoredKeys(keys.PublicKey, keys.PrivateKey))));
        }

        try
        {
            File.Move(draft, FilePath, overwrite: false);
            logger.LogInformation("Made the server's Web Push key pair");
        }
        catch (IOException exception) when (File.Exists(FilePath))
        {
            File.Delete(draft);
            logger.LogInformation(exception, "Another server process made the Web Push key pair first; using it");
        }
    }

    #endregion

    #region Stored Form

    private sealed record StoredKeys(string PublicKey, string PrivateKey);

    #endregion
}
