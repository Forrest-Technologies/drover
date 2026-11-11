namespace Drover.Server.Accounts;

/// <summary>
/// The one-time code that guards first-run setup. Generated at start while the server has no users and printed to
/// the log. A request from the same machine does not need it; every other request does.
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

    /// <summary>
    /// True for a request that comes from this machine and was not forwarded by a proxy. A proxy on the same
    /// machine makes every request look local, so a forwarding header turns the exemption off.
    /// </summary>
    public static bool IsFromThisMachine(HttpContext context)
    {
        var headers = context.Request.Headers;
        var forwarded = headers.ContainsKey("X-Forwarded-For") || headers.ContainsKey("Forwarded") || headers.ContainsKey("X-Real-IP");
        return !forwarded && context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
    }

    #endregion
}
