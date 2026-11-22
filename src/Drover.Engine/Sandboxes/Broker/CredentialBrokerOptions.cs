namespace Drover.Engine.Sandboxes.Broker;

/// <summary>
/// Where sandboxes reach the credential broker. Without a public host and port the broker is off: brokered secrets
/// are not given to sandboxes, raw ones still are, and every screen says so.
/// </summary>
public sealed class CredentialBrokerOptions
{
    #region Properties

    public const string Section = "Drover:SecretBroker";

    /// <summary>The host name sandboxes use for the broker. A name, not an address: providers allow egress by name.</summary>
    public string? PublicHost { get; set; }

    /// <summary>The port sandboxes use, which may differ from the one the server listens on behind a forwarder.</summary>
    public int PublicPort { get; set; } = 8443;

    /// <summary>The port the broker listens on.</summary>
    public int ListenPort { get; set; } = 8443;

    /// <summary>The broker listener's certificate and key, PEM files, for <see cref="PublicHost"/>.</summary>
    public string? CertificatePath { get; set; }

    public string? CertificateKeyPath { get; set; }

    /// <summary>The ports a sandbox may reach through the broker. Others are refused, so the server does not relay to every service on the internet.</summary>
    public List<int> AllowedPorts { get; set; } = [443, 80];

    /// <summary>
    /// Addresses the broker never relays to, beyond private ones: the server's own public addresses, and any whose
    /// owners trust this server by its address. The addresses <see cref="PublicHost"/> resolves to are always added.
    /// </summary>
    public List<string> RefusedAddresses { get; set; } = [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicHost) && !string.IsNullOrWhiteSpace(CertificatePath);

    #endregion
}
