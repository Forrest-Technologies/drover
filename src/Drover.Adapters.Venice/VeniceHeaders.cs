namespace Drover.Adapters.Venice;

/// <summary>Reads the rate-limit and balance headers Venice sends on inference responses.</summary>
internal static class VeniceHeaders
{
    #region Constants

    /// <summary>
    /// Seen on a real 401 from the service (the failed-request budget). The rate-limit guide documents a 429 as
    /// carrying <see cref="RateLimitResetRequests"/> instead. Neither has been seen on a real 429, so both are read.
    /// </summary>
    public const string RateLimitResets = "x-ratelimit-resets";

    /// <summary>Documented for inference 429 responses; unconfirmed against the live service.</summary>
    public const string RateLimitResetRequests = "x-ratelimit-reset-requests";

    public const string BalancePrefix = "x-venice-balance-";

    #endregion

    #region Public Methods

    /// <summary>
    /// How long until the request window clears. The header is a Unix timestamp in milliseconds.
    /// Returns null when the header is absent or unreadable.
    /// </summary>
    public static TimeSpan? WaitUntilReset(string? headerValue, DateTimeOffset now)
    {
        if (!long.TryParse(headerValue, NumberStyles.None, CultureInfo.InvariantCulture, out var resetMilliseconds))
        {
            return null;
        }

        var wait = DateTimeOffset.FromUnixTimeMilliseconds(resetMilliseconds) - now;

        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    /// <summary>Reads the wait from the first reset header present, in the order they are trusted.</summary>
    public static TimeSpan? WaitUntilReset(Func<string, string?> header, DateTimeOffset now) =>
        new[] { RateLimitResets, RateLimitResetRequests }
            .Select(name => WaitUntilReset(header(name), now))
            .FirstOrDefault(wait => wait is not null);

    /// <summary>Balances are reported one header per currency, for example <c>x-venice-balance-usd</c>.</summary>
    public static List<ModelBalance> ReadBalances(IEnumerable<KeyValuePair<string, string>> headers) =>
    [
        .. headers
            .Where(header => header.Key.StartsWith(BalancePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(header => (
                Currency: header.Key[BalancePrefix.Length..].ToLowerInvariant(),
                Parsed: decimal.TryParse(
                    header.Value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var amount),
                Amount: amount))
            .Where(balance => balance.Parsed)
            .Select(balance => new ModelBalance(balance.Currency, balance.Amount)),
    ];

    #endregion
}
