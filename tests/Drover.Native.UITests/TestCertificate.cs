namespace Drover.Native.UITests;

/// <summary>A certificate for the test server's loopback address, made for one run and trusted by its hash only.</summary>
internal static class TestCertificate
{
    #region Public Methods

    public static X509Certificate2 Create()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Drover screen checks", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

        // Windows serves TLS only with a key it can find again, so the certificate is read back from its export.
        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pkcs12), password: null);
    }

    /// <summary>The certificate's SHA-256 hash in hex, as the app compares it.</summary>
    public static string Hash(X509Certificate2 certificate) => certificate.GetCertHashString(HashAlgorithmName.SHA256);

    #endregion
}
