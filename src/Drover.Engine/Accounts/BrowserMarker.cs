namespace Drover.Engine.Accounts;

/// <summary>
/// What a browser's "signed in here before" marker says: the account it signed in to, and a digest of that
/// account's security stamp at the time. A password change or a reset changes the stamp, so a marker from before
/// it no longer names the account and the browser counts with everyone else.
/// </summary>
public sealed record BrowserMarker(Guid UserId, string StampDigest)
{
    #region Public Methods

    public static string Digest(string stamp) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)));

    /// <summary>Whether the marker was set for this account at its current security stamp.</summary>
    public bool Names(User user) =>
        user.Id == UserId
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(StampDigest),
            Encoding.ASCII.GetBytes(Digest(user.SecurityStamp ?? string.Empty)));

    #endregion
}
