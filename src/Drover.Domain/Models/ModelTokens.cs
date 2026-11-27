namespace Drover.Domain.Models;

/// <summary>
/// One person's token set for a model provider. It is stored as one JSON value in the vault, private to that person,
/// and the access token is what a call sends in place of a key. <see cref="Details"/> holds what the provider needs to
/// sign in again or to be called, such as a client id it issued or an account id; it is the adapter's own.
/// <see cref="AccountLabel"/> is what the provider says the account is called, shown to that person only.
/// </summary>
public sealed record ModelTokens
{
    #region Properties

    public required string AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    /// <summary>When the access token runs out, or null when the provider gave no end.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = [];

    public string? AccountLabel { get; init; }

    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    #endregion

    #region Public Methods

    public override string ToString() => $"{nameof(ModelTokens)} [secret]";

    #endregion
}
