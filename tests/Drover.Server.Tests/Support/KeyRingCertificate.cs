namespace Drover.Server.Tests.Support;

/// <summary>
/// A throwaway certificate and key in PEM files, which is how an operator gives the server a certificate to
/// protect the key ring with. One is made per test run and every test that needs one shares it.
/// </summary>
public static class KeyRingCertificate
{
    #region Private Fields

    private static readonly Lazy<(string Certificate, string Key)> files = new(Create);

    #endregion

    #region Properties

    public static string CertificatePath => files.Value.Certificate;

    public static string KeyPath => files.Value.Key;

    /// <summary>The settings that satisfy a preset which demands a protected key ring.</summary>
    public static Dictionary<string, string?> Settings => new()
    {
        ["DataProtection:CertificatePath"] = CertificatePath,
        ["DataProtection:CertificateKeyPath"] = KeyPath,
    };

    #endregion

    #region Private Methods

    private static (string Certificate, string Key) Create()
    {
        var directory = Directory.CreateTempSubdirectory("drover-cert-").FullName;
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=drover-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var certificatePath = Path.Combine(directory, "certificate.pem");
        var keyPath = Path.Combine(directory, "key.pem");
        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
        return (certificatePath, keyPath);
    }

    #endregion
}
