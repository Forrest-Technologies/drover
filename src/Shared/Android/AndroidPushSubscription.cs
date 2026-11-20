namespace Drover.App.Platforms.Android;

/// <summary>
/// One registration with a UnifiedPush distributor. <see cref="Endpoint"/> is the address the distributor gave last,
/// which messages come to, and <see cref="Reported"/> is the address the server was last told. They differ from the
/// moment a distributor rotates the address until the server has been told, and the difference is what is left to do.
/// </summary>
internal sealed record AndroidPushSubscription(
    string Token, string Distributor, Guid UserId, string PrivateKey, string PublicKey, string AuthSecret, string? Endpoint, string? Reported = null)
{
    #region Public Methods

    public static AndroidPushSubscription Create(string distributor, Guid userId)
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return new(
            Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
            distributor,
            userId,
            Base64Url.EncodeToString(key.ExportPkcs8PrivateKey()),
            Base64Url.EncodeToString(WebPushMessage.ExportPublicKey(key)),
            Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)),
            null);
    }

    /// <summary>True when the distributor has given an address the server has not been told yet.</summary>
    public bool HasUnreportedEndpoint => Endpoint is not null && Reported != Endpoint;

    public PushDeviceRequest? Request() => Endpoint is { } endpoint ? new(endpoint, PublicKey, AuthSecret) : null;

    public byte[] Open(byte[] payload)
    {
        using var key = ECDiffieHellman.Create();
        key.ImportPkcs8PrivateKey(Base64Url.DecodeFromChars(PrivateKey), out _);
        return WebPushMessage.Open(payload, key, Base64Url.DecodeFromChars(AuthSecret));
    }

    public static AndroidPushSubscription? Read(string? stored) =>
        stored?.Split('\n') is [var token, var distributor, var user, var privateKey, var publicKey, var authSecret, var endpoint, .. var rest]
            && rest.Length <= 1 && Guid.TryParse(user, out var userId)
            ? new(
                token,
                distributor,
                userId,
                privateKey,
                publicKey,
                authSecret,
                endpoint.Length > 0 ? endpoint : null,
                // A record kept before the server's address was remembered has told the server what it has.
                rest is [var reported] ? (reported.Length > 0 ? reported : null) : endpoint.Length > 0 ? endpoint : null)
            : null;

    public string Store() => string.Join(
        '\n', Token, Distributor, UserId.ToString(), PrivateKey, PublicKey, AuthSecret, Endpoint ?? string.Empty, Reported ?? string.Empty);

    public override string ToString() => nameof(AndroidPushSubscription);

    #endregion
}
