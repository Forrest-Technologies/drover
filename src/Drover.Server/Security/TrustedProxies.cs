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

    /// <summary>
    /// No IPv4 range wider than a /8 is accepted. This is a ceiling, not a check that the range is private: any /8
    /// passes, so the operator still names the narrowest range that holds the proxy.
    /// </summary>
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
        var slash = entry.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
        {
            options.KnownProxies.Add(ParsePlainAddress(entry, entry));
        }
        else
        {
            options.KnownIPNetworks.Add(ParseNetwork(entry, ParsePlainAddress(entry[..slash], entry), entry[(slash + 1)..]));
        }
    }

    /// <summary>
    /// Only the plain form is read: an IPv4 address in decimal with no leading zeros (which some parsers read as
    /// octal), and an IPv6 address with no zone. Anything else could be read as a different address than meant.
    /// </summary>
    private static IPAddress ParsePlainAddress(string text, string entry)
    {
        if (!IPAddress.TryParse(text, out var address))
        {
            throw NotPlain(entry);
        }

        var plain = address.AddressFamily is AddressFamily.InterNetwork
            ? IsPlainIPv4(text)
            : address.ScopeId == 0 && !text.Contains('%', StringComparison.Ordinal)
                && (!text.Contains('.', StringComparison.Ordinal) || IsPlainIPv4(text[(text.LastIndexOf(':') + 1)..]));
        return plain ? address : throw NotPlain(entry);
    }

    private static bool IsPlainIPv4(string text) =>
        IPAddress.TryParse(text, out var address) && address.AddressFamily is AddressFamily.InterNetwork
            && address.ToString() == text;

    /// <summary>
    /// A network is believed only as written: its address must be the network's first address, it must be no wider
    /// than <see cref="WidestIPv4Prefix"/> or <see cref="WidestIPv6Prefix"/> allows, and an IPv6 network must not
    /// take in the IPv4 addresses that reach the server written in IPv6 form.
    /// </summary>
    private static System.Net.IPNetwork ParseNetwork(string entry, IPAddress address, string prefixText)
    {
        var bits = address.GetAddressBytes().Length * 8;
        if (prefixText.Length is 0 || prefixText.Length > 3 || !prefixText.All(char.IsAsciiDigit)
            || (prefixText.Length > 1 && prefixText[0] is '0'))
        {
            throw NotPlain(entry);
        }

        var prefix = int.Parse(prefixText, NumberStyles.None, CultureInfo.InvariantCulture);
        if (prefix > bits)
        {
            throw NotPlain(entry);
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

        if (TakesInIPv4(address))
        {
            throw new InvalidOperationException(
                $"{Section}:TrustedAddresses holds '{entry}', which takes in IPv4 addresses: they reach the server written in IPv6 form, so this range could trust any IPv4 client. Write an IPv4 range as its IPv4 network, such as 10.0.0.0/24.");
        }

        return new(first, prefix);
    }

    /// <summary>
    /// The first 80 bits of every IPv4-mapped (<c>::ffff:0:0/96</c>) and IPv4-compatible (<c>::/96</c>) address are
    /// zero. A network whose own first address starts the same way holds or overlaps them, whatever its prefix.
    /// </summary>
    private static bool TakesInIPv4(IPAddress network) =>
        network.AddressFamily is AddressFamily.InterNetworkV6 && network.GetAddressBytes().AsSpan(0, 10).IndexOfAnyExcept((byte)0) < 0;

    private static IPAddress FirstAddress(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();
        for (var bit = prefix; bit < bytes.Length * 8; bit++)
        {
            bytes[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        }

        return new(bytes);
    }

    private static InvalidOperationException NotPlain(string entry) =>
        new($"{Section}:TrustedAddresses holds '{entry}', which is not a plain address or network: write an address such as 10.0.0.5 or fd00::5, or a network such as 10.0.0.0/24 or fd00::/64, in decimal with no leading zeros and no zone.");

    #endregion
}
