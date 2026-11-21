namespace Drover.App.Platforms.Android;

/// <summary>
/// A write to the server for one person on one server, made only while that person is still signed in on that server.
/// The server takes a write as coming from whoever is signed in when it arrives, and a person can sign out and another
/// in between a check and a write, so the check is made before the write and again after it. A write that is found to
/// have been made for someone else is undone, and what is undone is exactly what was written.
/// </summary>
public static class OwnerBoundWrite
{
    #region Public Methods

    /// <summary>
    /// Runs <paramref name="write"/> if <paramref name="owner"/> is signed in on <paramref name="server"/>, and checks
    /// again after it. True when the write was made for the owner. False when it was not made, or was made for another
    /// person or on another server and was then undone with <paramref name="undo"/>, which is tried once and whose
    /// failure to reach the server is left to the server's own expiry of a registration nobody uses.
    /// </summary>
    public static async Task<bool> Run(
        Func<Uri?> server,
        Uri origin,
        Func<Task<Guid?>> signedIn,
        Guid owner,
        Func<Task> write,
        Func<Task> undo,
        ILogger? logger = null)
    {
        if (!await IsOwner(server, origin, signedIn, owner))
        {
            return false;
        }

        await write();
        if (await IsOwner(server, origin, signedIn, owner))
        {
            return true;
        }

        try
        {
            await undo();
        }
        catch (Exception exception) when (exception is HttpRequestException or DroverApiException or OperationCanceledException)
        {
            // The write is the old person's; the server keeps it until it finds the address unused. It is not
            // recorded as made, so it is not mistaken for the new person's.
            logger?.LogWarning(exception, "A notification registration written for a person who signed out could not be undone");
        }

        return false;
    }

    #endregion

    #region Private Methods

    private static async Task<bool> IsOwner(Func<Uri?> server, Uri origin, Func<Task<Guid?>> signedIn, Guid owner) =>
        server() == origin && await signedIn() == owner;

    #endregion
}
