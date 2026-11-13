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
        var entries = configuration.GetSection(Section).Get<TrustedProxyOptions>()?.TrustedAddresses ?? [];
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

    /// <summary>A private IPv4 range is at most a /8; anything wider takes in addresses on the open internet.</summary>
    public const int WidestIPv4Prefix = 8;

    /// <summary>A /32 is what one provider is given for all its customers; anything wider names no one's network.</summary>
    public const int WidestIPv6Prefix = 32;

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
        if (entry.Contains('/', StringComparison.Ordinal))
        {
            options.KnownIPNetworks.Add(ParseNetwork(entry));
        }
        else if (IPAddress.TryParse(entry, out var address))
        {
            options.KnownProxies.Add(address);
        }
        else
        {
            throw NotAnAddressOrNetwork(entry);
        }
    }

    /// <summary>
    /// A network is believed only as written: its address must be the network's first address, and it must be no
    /// wider than <see cref="WidestIPv4Prefix"/> or <see cref="WidestIPv6Prefix"/> allows.
    /// </summary>
    private static System.Net.IPNetwork ParseNetwork(string entry)
    {
        var parts = entry.Split('/');
        if (parts.Length is not 2 || !IPAddress.TryParse(parts[0], out var address)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            || prefix > address.GetAddressBytes().Length * 8)
        {
            throw NotAnAddressOrNetwork(entry);
        }

        var widest = address.AddressFamily is AddressFamily.InterNetwork ? WidestIPv4Prefix : WidestIPv6Prefix;
        if (prefix < widest)
        {
            throw new InvalidOperationException(
                $"{Section}:TrustedAddresses holds '{entry}', which is too wide to name a proxy: any address in it could claim to be any client and connect over HTTPS, and the sign-in limit could be dodged from it. Name the proxy's own network, no wider than /{widest}.");
        }

        var first = FirstAddress(address, prefix);
        if (!first.Equals(address))
        {
            throw new InvalidOperationException(
                $"{Section}:TrustedAddresses holds '{entry}', which names an address inside a network rather than the network. Write it as {first}/{prefix}, or name the one address without a prefix.");
        }

        return new(first, prefix);
    }

    private static IPAddress FirstAddress(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();
        for (var bit = prefix; bit < bytes.Length * 8; bit++)
        {
            bytes[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        }

        return new(bytes);
    }

    private static InvalidOperationException NotAnAddressOrNetwork(string entry) =>
        new($"{Section}:TrustedAddresses holds '{entry}', which is neither an address nor a network such as 10.0.0.0/8.");

    #endregion
}
