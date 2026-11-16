namespace Drover.Adapters.WebPush;

/// <summary>
/// The server's VAPID key pair, base64url, and the contact it signs with. One pair for the whole server: a browser
/// holds one subscription per site, made with this public key, whatever space its person works in. The private key
/// is a secret: it is never logged, never returned, and its text is never shown.
/// </summary>
public sealed class WebPushKeys
{
    #region Properties

    public required string PublicKey { get; init; }

    public required string PrivateKey { get; init; }

    /// <summary>How a push service reaches the operator: an https address or a mailto address.</summary>
    public required string Subject { get; init; }

    #endregion

    #region Public Methods

    /// <summary>A new P-256 pair, as VAPID needs: the public key uncompressed, the private key as its scalar.</summary>
    public static WebPushKeys Generate(string subject)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        byte[] publicKey = [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];
        return new WebPushKeys
        {
            PublicKey = Base64Url.EncodeToString(publicKey),
            PrivateKey = Base64Url.EncodeToString(parameters.D!),
            Subject = subject,
        };
    }

    public override string ToString() => "[web push keys]";

    #endregion
}
