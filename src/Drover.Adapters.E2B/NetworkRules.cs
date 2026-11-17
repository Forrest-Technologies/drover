namespace Drover.Adapters.E2B;

/// <summary>
/// Maps Drover's network policy to E2B egress rules. E2B lets an allowed entry beat a denied one, takes only addresses
/// and CIDR blocks in <c>denyOut</c>, and replaces every rule on update, so each mapping is complete on its own.
/// </summary>
internal static partial class NetworkRules
{
    #region Private Fields

    /// <summary>The SDK's <c>ALL_TRAFFIC</c>. Denying it is what <c>allow_internet_access: false</c> means.</summary>
    public const string AllTraffic = "0.0.0.0/0";

    /// <summary>Private, carrier-grade NAT, and link-local (cloud metadata) ranges, closed even when the network is open.</summary>
    public static readonly IReadOnlyList<string> PrivateRanges =
        ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10", "169.254.0.0/16"];

    #endregion

    #region Public Methods

    public static NetworkConfig ForCreate(NetworkPolicy policy) => From(policy) with { AllowPublicTraffic = false };

    public static NetworkConfig ForUpdate(NetworkPolicy policy) => From(policy);

    #endregion

    #region Private Methods

    private static NetworkConfig From(NetworkPolicy policy) =>
        policy.Access switch
        {
            NetworkAccess.Off => new(null, [AllTraffic], null),
            NetworkAccess.Allowlist => new(AllowedDomains(policy.AllowedHosts), [AllTraffic], null),
            NetworkAccess.Open => new(null, PrivateRanges, null),
            _ => throw new SandboxProviderException(SandboxFailure.Rejected, "The sandbox network setting is not one Drover knows."),
        };

    /// <summary>
    /// Domain names only, optionally with a leading <c>*.</c>. An address or CIDR block would beat every deny rule, so
    /// an allowlist entry like <c>10.0.0.0/8</c> would open the private ranges. Those are refused, not passed on.
    /// </summary>
    private static List<string> AllowedDomains(IReadOnlyList<string> hosts)
    {
        List<string> domains = [.. hosts.Select(host => host.Trim().ToLowerInvariant())];

        if (domains.Any(domain => !DomainName().IsMatch(domain)))
        {
            throw new SandboxProviderException(
                SandboxFailure.Rejected,
                "Allowed sandbox hosts must be domain names such as example.com or *.example.com.");
        }

        return domains;
    }

    [GeneratedRegex(@"^(\*\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex DomainName();

    #endregion
}
