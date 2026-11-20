namespace Drover.Native.Platform;

/// <summary>
/// Where the sign-in that outlives the app is kept on a desktop: the operating system's credential store, opened the
/// first time the session asks for it. When the computer has no store Drover can use (a Linux machine with no Secret
/// Service, for one), or the store fails later, the app says so once and keeps the sign-in only while it is open, as
/// it did before there was a store. Nothing is ever written to a plain file, and no secret reaches the log.
/// </summary>
internal sealed class DesktopSecrets(Func<ISecureStorage> openSystemStore, ILogger<DesktopSecrets> logger) : ISecureStorage
{
    #region Private Fields

    private const string NoStore =
        "Drover cannot use a credential store on this computer, so the sign-in lasts until the app closes";

    private readonly Lock gate = new();

    private readonly Dictionary<string, string> untilClosed = [];

    private ISecureStorage? system;

    private bool chosen;

    #endregion

    #region Interface Implementations

    // The names are the ones the shared session code calls.
#pragma warning disable DRV0001
    public async Task<string?> GetAsync(string key)
    {
        if (Chosen() is { } store)
        {
            try
            {
                return await store.GetAsync(key);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (GiveUpOnStore())
                {
                    logger.LogWarning(exception, NoStore);
                }
            }
        }

        lock (gate)
        {
            return untilClosed.GetValueOrDefault(key);
        }
    }

    public async Task SetAsync(string key, string value)
    {
        if (Chosen() is { } store)
        {
            try
            {
                await store.SetAsync(key, value);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (GiveUpOnStore())
                {
                    logger.LogWarning(exception, NoStore);
                }
            }
        }

        lock (gate)
        {
            untilClosed[key] = value;
        }
    }
#pragma warning restore DRV0001

    public bool Remove(string key)
    {
        if (Chosen() is { } store)
        {
            try
            {
                return store.Remove(key);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (GiveUpOnStore())
                {
                    logger.LogWarning(exception, NoStore);
                }
            }
        }

        lock (gate)
        {
            return untilClosed.Remove(key);
        }
    }

    #endregion

    #region Private Methods

    private ISecureStorage? Chosen()
    {
        lock (gate)
        {
            if (chosen)
            {
                return system;
            }

            chosen = true;
            try
            {
                system = openSystemStore();
                logger.LogInformation("The Drover sign-in is kept in the system's credential store");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, NoStore);
            }

            return system;
        }
    }

    /// <summary>True when the store was in use until now, so this is the moment to say it is not.</summary>
    private bool GiveUpOnStore()
    {
        lock (gate)
        {
            var wasInUse = system is not null;
            system = null;
            return wasInUse;
        }
    }

    #endregion
}
