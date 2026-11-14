namespace Drover.Engine.Accounts;

/// <summary>
/// A stored hash of a random password nobody knows. A guess that must not be checked against the real password
/// (an unknown email, a locked bucket, an attempt over the limit) is checked against this instead, at the same cost.
/// </summary>
public static class DecoyPassword
{
    #region Private Fields

    private static readonly Lazy<string> hash =
        new(() => new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString("N")));

    #endregion

    #region Properties

    public static string Hash => hash.Value;

    #endregion
}
