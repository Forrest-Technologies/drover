namespace Drover.Domain.Sandboxes;

/// <summary>
/// The random values a sandbox is given for the credential broker: the token it proves itself with, and the
/// placeholder each brokered secret stands under. Only hashes are stored; a value is unusable from any other sandbox,
/// because the broker looks a placeholder up among the entries of the sandbox that sent it.
/// </summary>
public static partial class BrokerTokens
{
    #region Properties

    public const string PlaceholderPrefix = "drover_ph_";

    /// <summary>The user name a sandbox gives the broker; the password is its token.</summary>
    public const string ProxyUser = "sandbox";

    #endregion

    #region Public Methods

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string NewPlaceholder() =>
        $"{PlaceholderPrefix}{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";

    public static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    /// <summary>Every placeholder in a text, each once.</summary>
    public static IReadOnlyList<string> FindPlaceholders(string text) =>
        text.Contains(PlaceholderPrefix, StringComparison.Ordinal)
            ? [.. Placeholder().Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal)]
            : [];

    #endregion

    #region Private Methods

    [GeneratedRegex("drover_ph_[0-9a-f]{32}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    #endregion
}
