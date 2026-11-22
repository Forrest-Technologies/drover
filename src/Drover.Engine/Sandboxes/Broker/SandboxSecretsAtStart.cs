namespace Drover.Engine.Sandboxes.Broker;

/// <summary>
/// What a new sandbox is given for its secrets: each name with a placeholder (brokered) or the value (raw), the
/// broker's address with a token only this sandbox has, and the settings that make common tools trust the space's
/// broker authority. Built in a tenant transaction as the run's person; the raw values are held only for the provider
/// call that creates the sandbox.
/// </summary>
public sealed record SandboxSecretsAtStart(
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<SandboxSecretEntry> Entries,
    byte[]? TokenHash,
    string? Authority)
{
    #region Properties

    public const string TrustFolder = "/tmp/drover-broker";

    public const string AuthorityPath = $"{TrustFolder}/authority.pem";

    public const string BundlePath = $"{TrustFolder}/ca-bundle.pem";

    public static SandboxSecretsAtStart None { get; } = new(new Dictionary<string, string>(), [], null, null);

    /// <summary>Whether the sandbox goes through the broker, so its egress is locked to it.</summary>
    public bool UsesBroker => TokenHash is not null;

    /// <summary>
    /// The fixed command, written by Drover and holding no model text, that joins the system's trusted roots with the
    /// space's authority into the bundle the environment names.
    /// </summary>
    public static string BundleScript =>
        $"""
        set -e
        for roots in /etc/ssl/certs/ca-certificates.crt /etc/pki/tls/certs/ca-bundle.crt /etc/ssl/cert.pem /etc/ssl/ca-bundle.pem; do
          if [ -f "$roots" ]; then cat "$roots" > {BundlePath}.new; break; fi
        done
        touch {BundlePath}.new
        cat {AuthorityPath} >> {BundlePath}.new
        mv {BundlePath}.new {BundlePath}
        """;

    #endregion

    #region Public Methods

    /// <summary>
    /// The secrets for a sandbox owned by the bot <paramref name="ownerBotId"/>. For each name the narrowest wins: a value given to
    /// the bot, else the project values and the space value together under one placeholder (the broker picks the one
    /// for the project at work, else the space's), else a raw space value alone. Brokered values are left out when the
    /// broker is not set up.
    /// </summary>
    public static async Task<SandboxSecretsAtStart> Prepare(
        IServiceProvider services,
        Guid sandboxId,
        Guid ownerBotId,
        CredentialBrokerOptions broker,
        BrokerAuthorities authorities,
        CancellationToken cancellationToken)
    {
        var reaching = await services.GetRequiredService<SandboxSecretRepository>().ListSecretsReachingBot(ownerBotId, cancellationToken);

        if (reaching.Count == 0)
        {
            return None;
        }

        var vault = services.GetRequiredService<ISecretVault>();
        Dictionary<string, string> environment = [];
        List<SandboxSecretEntry> entries = [];

        foreach (var chosen in reaching.GroupBy(secret => secret.Name, StringComparer.Ordinal).Select(Choose))
        {
            if (chosen is [{ Mode: SandboxSecretMode.Raw } raw])
            {
                environment[raw.Name] = await vault.Reveal(raw.SecretId, cancellationToken);
                entries.Add(new SandboxSecretEntry { SandboxId = sandboxId, SandboxSecretId = raw.Id });
            }
            else if (broker.IsConfigured && chosen.Count > 0)
            {
                var placeholder = BrokerTokens.NewPlaceholder();
                environment[chosen[0].Name] = placeholder;
                entries.AddRange(chosen.Select(secret => new SandboxSecretEntry
                {
                    SandboxId = sandboxId,
                    SandboxSecretId = secret.Id,
                    PlaceholderHash = BrokerTokens.Hash(placeholder),
                }));
            }
        }

        if (entries.All(entry => entry.PlaceholderHash is null))
        {
            return new(environment, entries, null, null);
        }

        var token = BrokerTokens.NewToken();
        var proxy = $"https://{BrokerTokens.ProxyUser}:{token}@{broker.PublicHost}:{broker.PublicPort}";

        foreach (var name in (string[])["HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy"])
        {
            environment[name] = proxy;
        }

        foreach (var name in (string[])["SSL_CERT_FILE", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE", "GIT_SSL_CAINFO", "AWS_CA_BUNDLE"])
        {
            environment[name] = BundlePath;
        }

        environment["NODE_EXTRA_CA_CERTS"] = AuthorityPath;
        var authority = await authorities.CertificateFor(services, cancellationToken);
        return new(environment, entries, BrokerTokens.Hash(token), authority);
    }

    #endregion

    #region Private Methods

    private static List<SandboxSecret> Choose(IGrouping<string, SandboxSecret> named)
    {
        if (named.FirstOrDefault(secret => secret.Scope is SandboxSecretScope.Bots) is { } forBot)
        {
            return [forBot];
        }

        var forSpace = named.FirstOrDefault(secret => secret.Scope is SandboxSecretScope.Space);

        if (forSpace is { Mode: SandboxSecretMode.Raw })
        {
            return [forSpace];
        }

        return [.. named.Where(secret => secret.Mode is SandboxSecretMode.Brokered)];
    }

    #endregion
}
