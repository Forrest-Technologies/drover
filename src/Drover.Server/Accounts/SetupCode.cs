namespace Drover.Server.Accounts;

/// <summary>
/// The one-time code that guards first-run setup. Generated at start while the server has no users and printed to
/// the log. Every setup request needs it, including one from this machine: a reverse proxy or an SSH tunnel on the
/// same host makes a remote request look local, so the address proves nothing.
/// </summary>
public sealed class SetupCode
{
    #region Private Fields

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly string value = RandomNumberGenerator.GetString(Alphabet, 12);

    #endregion

    #region Properties

    public string Display => $"{value[..4]}-{value[4..8]}-{value[8..]}";

    #endregion

    #region Public Methods

    public bool Matches(string? candidate)
    {
        var normalized = candidate?.Replace("-", string.Empty).Trim().ToUpperInvariant() ?? string.Empty;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(normalized), Encoding.UTF8.GetBytes(value));
    }

    #endregion
}
