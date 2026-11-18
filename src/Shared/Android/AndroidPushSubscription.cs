namespace Drover.App.Platforms.Android;

internal sealed record AndroidPushSubscription(
    string Token, string Distributor, Guid UserId, string PrivateKey, string PublicKey, string AuthSecret, string? Endpoint)
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

    public PushDeviceRequest? Request() => Endpoint is { } endpoint ? new(endpoint, PublicKey, AuthSecret) : null;

    public byte[] Open(byte[] payload)
    {
        using var key = ECDiffieHellman.Create();
        key.ImportPkcs8PrivateKey(Base64Url.DecodeFromChars(PrivateKey), out _);
        return WebPushMessage.Open(payload, key, Base64Url.DecodeFromChars(AuthSecret));
    }

    public static AndroidPushSubscription? Read(string? stored) =>
        stored?.Split('\n') is [var token, var distributor, var user, var privateKey, var publicKey, var authSecret, var endpoint]
            && Guid.TryParse(user, out var userId)
            ? new(token, distributor, userId, privateKey, publicKey, authSecret, endpoint.Length > 0 ? endpoint : null)
            : null;

    public string Store() => string.Join('\n', Token, Distributor, UserId.ToString(), PrivateKey, PublicKey, AuthSecret, Endpoint ?? string.Empty);

    public override string ToString() => nameof(AndroidPushSubscription);

    #endregion
}
