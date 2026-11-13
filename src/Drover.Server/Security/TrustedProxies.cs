namespace Drover.Server.Security;

/// <summary>The reverse proxies in front of this server, each an address or a network in CIDR form.</summary>
public sealed class TrustedProxyOptions
{
    public List<string> TrustedAddresses { get; set; } = [];
}

/// <summary>
/// Forwarded headers are believed only when the request came from a proxy the operator named. With none named,
/// none are read: a client cannot claim an address or a scheme by sending a header.
/// </summary>
public static class TrustedProxies
{
    #region Public Methods

    public static IServiceCollection AddTrustedProxies(this IServiceCollection services, IConfiguration configuration)
    {
        // An entry left empty names no proxy: a deployment file can pass a variable through that nobody set.
        List<string> entries =
        [
            .. (configuration.GetSection(Section).Get<TrustedProxyOptions>()?.TrustedAddresses ?? []).Where(entry => !string.IsNullOrWhiteSpace(entry)),
        ];
        RefuseTheHostSwitch(configuration);
        services.Configure<TrustedProxyOptions>(options => options.TrustedAddresses = entries);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // With no proxy named, no header is read by anything that uses these options.
            options.ForwardedHeaders = entries.Count == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            // The defaults trust loopback. Nothing is trusted here unless the operator names it.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var entry in entries)
            {
                options.Trust(entry);
            }
        });
        return services;
    }

    public static IApplicationBuilder UseTrustedProxies(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;
        if (options.TrustedAddresses.Count > 0)
        {
            return app.UseForwardedHeaders();
        }

        app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Drover.Startup").LogInformation(
            "No reverse proxy is trusted. Behind one, set {Setting} to its address or network, or every person shares its address for the sign-in limit",
            $"{Section}:TrustedAddresses");
        return app;
    }

    #endregion

    #region Constants

    public const string Section = "Drover:Proxy";

    public const string HostSwitch = "ForwardedHeaders_Enabled";

    #endregion

    #region Private Methods

    /// <summary>
    /// The host's own switch adds a second forwarded-headers step that this configuration would leave trusting
    /// every address. The server does not start with it on.
    /// </summary>
    private static void RefuseTheHostSwitch(IConfiguration configuration)
    {
        if (string.Equals(configuration[HostSwitch], "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"ASPNETCORE_FORWARDEDHEADERS_ENABLED is on. Turn it off and name the proxy in {Section}:TrustedAddresses instead.");
        }
    }

    private static void Trust(this ForwardedHeadersOptions options, string entry)
    {
        if (System.Net.IPNetwork.TryParse(entry, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
        else if (IPAddress.TryParse(entry, out var address))
        {
            options.KnownProxies.Add(address);
        }
        else
        {
            throw new InvalidOperationException(
                $"{Section}:TrustedAddresses holds '{entry}', which is neither an address nor a network such as 10.0.0.0/8.");
        }
    }

    #endregion
}
