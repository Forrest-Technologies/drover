namespace Drover.Server.Sandboxes;

public static class SandboxServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the sandbox provider and the sandbox tools when the operator has configured a provider key, and
    /// nothing when not: a server without a key has no sandboxes, which is not an error. The key is read from
    /// <c>Drover:Sandbox:E2B:ApiKey</c>, or from the file <c>Drover:Sandbox:E2B:ApiKeyFile</c> names (a container
    /// secret), and held only in the adapter's options in memory: never in the database, the vault, a response, or
    /// the log. A key that is plainly not an E2B key stops the server, naming the setting and never the value. A
    /// provider the deployment policy's <see cref="DeploymentPolicy.SandboxAdapters"/> leaves out is not registered.
    /// </summary>
    public static IServiceCollection AddDroverSandboxes(this IServiceCollection services, IConfiguration configuration, DeploymentPolicy policy)
    {
        if (ReadKey(configuration) is not { } key)
        {
            return services;
        }

        // E2B runs each sandbox in its own virtual machine, a hard isolation boundary.
        if (!policy.SandboxAdapters.Allows(hardBoundary: true))
        {
            return services;
        }

        var section = configuration.GetSection(Section);
        services.AddE2BSandboxProvider(options =>
        {
            options.ApiKey = key;
            options.Domain = section["Domain"] is { Length: > 0 } domain ? domain : options.Domain;
            options.Template = section["Template"] is { Length: > 0 } template ? template : options.Template;
            options.DesktopTemplate = section["DesktopTemplate"] is { Length: > 0 } desktop ? desktop : options.DesktopTemplate;
        });
        services.AddSandboxTools();

        // Retired sandboxes and interrupted creations are cleared up whether or not their bot is used again.
        services.AddSingleton<SandboxSweep>();
        services.AddHostedService<SandboxSweeper>();
        services.AddCredentialBroker(configuration, policy);
        return services;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// The credential broker, when the operator set it up: a public host name sandboxes reach it by, and a certificate
    /// for that name. Half a setup stops the server, naming what is missing. Without one, brokered secrets are not
    /// given to sandboxes and raw ones still are.
    /// </summary>
    private static IServiceCollection AddCredentialBroker(this IServiceCollection services, IConfiguration configuration, DeploymentPolicy policy)
    {
        var section = configuration.GetSection(CredentialBrokerOptions.Section);
        var broker = new CredentialBrokerOptions();
        section.Bind(broker);
        services.Configure<CredentialBrokerOptions>(section);

        if (string.IsNullOrWhiteSpace(broker.PublicHost) && string.IsNullOrWhiteSpace(broker.CertificatePath))
        {
            return services;
        }

        if (!broker.IsConfigured)
        {
            throw new InvalidOperationException(
                $"The credential broker needs both {CredentialBrokerOptions.Section}:PublicHost and {CredentialBrokerOptions.Section}:CertificatePath. Set both, or neither to turn it off.");
        }

        if (SandboxSecretRules.HostProblem(SandboxSecretRules.NormalizeHost(broker.PublicHost!)) is not null || broker.PublicHost!.StartsWith("*.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{CredentialBrokerOptions.Section}:PublicHost must be the public host name sandboxes reach the broker by, such as broker.example.com.");
        }

        CredentialBrokerServer.ReadCertificate(broker).Dispose();
        services.AddSingleton(provider => BrokerUpstream.ForPublicAddresses(
            policy,
            RefusedAddresses(broker, provider.GetRequiredService<ILogger<CredentialBrokerServer>>())));
        services.AddSingleton<BrokerLimits>();
        services.AddSingleton<CredentialBrokerServer>();
        services.AddHostedService(provider => provider.GetRequiredService<CredentialBrokerServer>());
        return services;
    }

    /// <summary>
    /// The server's own public addresses, from its broker host name, and the operator's list. A listed entry that is not
    /// an address stops the server, naming the setting.
    /// </summary>
    private static List<IPAddress> RefusedAddresses(CredentialBrokerOptions broker, ILogger logger)
    {
        List<IPAddress> refused = [];

        foreach (var entry in broker.RefusedAddresses)
        {
            refused.Add(IPAddress.TryParse(entry, out var address)
                ? address
                : throw new InvalidOperationException($"{CredentialBrokerOptions.Section}:RefusedAddresses holds an entry that is not an address. List addresses only."));
        }

        try
        {
            refused.AddRange(Dns.GetHostAddresses(broker.PublicHost!));
        }
        catch (SocketException unresolved)
        {
            // The name may not resolve yet on first start; its addresses are then not on the list until a restart.
            logger.LogWarning("The broker host name could not be resolved at start ({Error}); list this server's addresses in RefusedAddresses", unresolved.SocketErrorCode);
        }

        return refused;
    }

    private static string? ReadKey(IConfiguration configuration)
    {
        var value = configuration[KeySetting];
        var path = configuration[KeyFileSetting];

        var key = (string.IsNullOrWhiteSpace(value), string.IsNullOrWhiteSpace(path)) switch
        {
            (true, true) => null,
            (false, false) => throw new InvalidOperationException(
                $"Both {KeySetting} and {KeyFileSetting} are set. Give the sandbox provider key one way only."),
            (false, true) => value!.Trim(),
            (true, false) => ReadKeyFile(path!),
        };

        return key is null || E2BOptions.IsWellFormedKey(key)
            ? key
            : throw new InvalidOperationException(
                $"{(string.IsNullOrWhiteSpace(value) ? KeyFileSetting : KeySetting)} does not hold an E2B API key, which starts with e2b_. Correct the setting, or remove it to run without sandboxes.");
    }

    private static string ReadKeyFile(string path) =>
        File.Exists(path)
            ? File.ReadAllText(path).Trim()
            : throw new InvalidOperationException($"{KeyFileSetting} must name a readable file holding the sandbox provider key.");

    #endregion

    #region Constants

    public const string KeySetting = "Drover:Sandbox:E2B:ApiKey";

    public const string KeyFileSetting = "Drover:Sandbox:E2B:ApiKeyFile";

    private const string Section = "Drover:Sandbox:E2B";

    #endregion
}
