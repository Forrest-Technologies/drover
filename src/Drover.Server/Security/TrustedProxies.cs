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
        services.Configure<TrustedProxyOptions>(options => options.TrustedAddresses = entries);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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
        return options.TrustedAddresses.Count == 0 ? app : app.UseForwardedHeaders();
    }

    #endregion

    #region Constants

    public const string Section = "Drover:Proxy";

    #endregion

    #region Private Methods

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
