namespace Drover.Engine.Sandboxes.Broker;

/// <summary>
/// Each space's certificate authority for the credential broker, and the certificates it signs for the hosts a
/// secret is bound to. The private key is kept in the vault; the signing certificate and the host certificates live
/// in memory only and are never written to disk. Sandboxes of a space trust that space's authority and no other.
/// </summary>
public sealed class BrokerAuthorities(TimeProvider time)
{
    #region Private Fields

    private static readonly TimeSpan lifetime = TimeSpan.FromDays(365);

    private static readonly TimeSpan renewBefore = TimeSpan.FromDays(30);

    private static readonly TimeSpan hostLifetime = TimeSpan.FromDays(7);

    private static readonly TimeSpan cacheFor = TimeSpan.FromHours(1);

    private const int MaxCachedHosts = 4096;

    private readonly ConcurrentDictionary<Guid, Cached<X509Certificate2>> signers = new();

    private readonly ConcurrentDictionary<(Guid SpaceId, string Host), Cached<X509Certificate2>> hosts = new();

    #endregion

    #region Public Methods

    /// <summary>
    /// The space's authority certificates still valid, PEM encoded, for a sandbox's trust bundle. A new authority is
    /// made when none will still be valid in thirty days; the older one stays in the bundle until it ends, so sandboxes
    /// started before the renewal keep working. Runs inside a tenant scope with a person set, which the vault needs.
    /// </summary>
    public async Task<string> CertificateFor(IServiceProvider services, CancellationToken cancellationToken)
    {
        var brokers = services.GetRequiredService<CredentialBrokerRepository>();
        var now = time.GetUtcNow();
        var made = string.Empty;

        if (await brokers.FindAuthority(now + renewBefore, cancellationToken) is null)
        {
            await brokers.LockAuthority(cancellationToken);

            if (await brokers.FindAuthority(now + renewBefore, cancellationToken) is null)
            {
                made = await Make(services, brokers, now, cancellationToken);
            }
        }

        // An authority made just now is saved with the transaction, so it is not yet among those read back.
        return string.Concat([.. (await brokers.ListAuthorities(now, cancellationToken)).Select(authority => authority.Certificate), made]);
    }

    /// <summary>
    /// A certificate for <paramref name="host"/> signed by the space's authority, with its key, for the broker to
    /// present to a sandbox. Null when the space has no authority yet, which means none of its sandboxes trusts one.
    /// Runs inside a tenant scope of the space.
    /// </summary>
    public async Task<X509Certificate2?> HostCertificate(IServiceProvider services, Guid spaceId, string host, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        if (hosts.TryGetValue((spaceId, host), out var cached) && cached.Until > now)
        {
            return cached.Value;
        }

        if (await Signer(services, spaceId, now, cancellationToken) is not { } signer)
        {
            return null;
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var notAfter = now + hostLifetime < signer.NotAfter.ToUniversalTime() ? now + hostLifetime : signer.NotAfter.ToUniversalTime();
        using var signed = request.Create(signer, now.AddHours(-1), notAfter, RandomNumberGenerator.GetBytes(16));
        using var withKey = signed.CopyWithPrivateKey(key);

        // Some platforms present a certificate in a TLS handshake only when its key came from a key store, so it is
        // loaded again from an in-memory PKCS #12 blob, which is never written anywhere.
        var usable = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pkcs12), null);

        // A wildcard binding lets a sandbox ask for any number of names; the cache keeps a bounded number.
        if (hosts.Count >= MaxCachedHosts)
        {
            hosts.Clear();
        }

        hosts[(spaceId, host)] = new(usable, now + cacheFor);
        return usable;
    }

    #endregion

    #region Private Methods

    private async Task<string> Make(IServiceProvider services, CredentialBrokerRepository brokers, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenant = services.GetRequiredService<TenantContext>();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=Drover sandbox broker, OU={tenant.SpaceId}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var certificate = request.CreateSelfSigned(now.AddHours(-1), now + lifetime);
        var secretId = await services.GetRequiredService<ISecretVault>().Store(SecretKind.BrokerAuthority, key.ExportPkcs8PrivateKeyPem(), null, cancellationToken);
        var pem = certificate.ExportCertificatePem();
        brokers.AddAuthority(new BrokerAuthority
        {
            Certificate = pem,
            SecretId = secretId,
            NotAfter = certificate.NotAfter.ToUniversalTime(),
            CreatedAt = now,
        });
        return pem;
    }

    private async Task<X509Certificate2?> Signer(IServiceProvider services, Guid spaceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (signers.TryGetValue(spaceId, out var cached) && cached.Until > now)
        {
            return cached.Value;
        }

        // The oldest authority still valid for a host certificate's whole life: a sandbox trusts every authority that
        // was valid when it started, and the oldest is the one every sandbox still alive was started with.
        if ((await services.GetRequiredService<CredentialBrokerRepository>().ListAuthorities(now + hostLifetime, cancellationToken)).FirstOrDefault() is not { } authority)
        {
            return null;
        }

        var key = await services.GetRequiredService<ISecretVault>().Reveal(authority.SecretId, cancellationToken);
        var signer = X509Certificate2.CreateFromPem(authority.Certificate, key);
        signers[spaceId] = new(signer, now + cacheFor);
        return signer;
    }

    #endregion

    #region Nested Types

    private sealed record Cached<T>(T Value, DateTimeOffset Until);

    #endregion
}
