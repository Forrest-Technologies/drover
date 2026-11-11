namespace Drover.Engine.Policy;

/// <summary>
/// Which addresses an outbound request must not reach when the deployment forbids private addresses: loopback,
/// private, link-local (which holds the cloud metadata address), shared address space, unspecified, multicast,
/// and anything else that is not a public unicast address. An IPv6 address that wraps an IPv4 one is judged by
/// the IPv4 address inside it.
/// </summary>
public static class PrivateAddresses
{
    #region Private Fields

    private static readonly byte[] nat64Prefix = [0, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];

    #endregion

    #region Public Methods

    public static bool Contains(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return Contains(address.MapToIPv4());
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateIPv4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPrivateIPv6(address),
            _ => true,
        };
    }

    #endregion

    #region Private Methods

    private static bool IsPrivateIPv4(byte[] bytes) =>
        bytes switch
        {
            [0, ..] => true,
            [10, ..] => true,
            [100, >= 64 and <= 127, ..] => true,
            [127, ..] => true,
            [169, 254, ..] => true,
            [172, >= 16 and <= 31, ..] => true,
            [192, 0, 0, ..] => true,
            [192, 168, ..] => true,
            [198, 18 or 19, ..] => true,
            [>= 224, ..] => true,
            _ => false,
        };

    private static bool IsPrivateIPv6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Loopback) || address.IsIPv6Multicast)
        {
            return true;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal)
        {
            return true;
        }

        return IsPrivateEmbeddedIPv4(address.GetAddressBytes());
    }

    // ::/96 (deprecated IPv4-compatible) is refused outright. 64:ff9b::/96 (NAT64) and 2002::/16 (6to4) carry an
    // IPv4 address, which is judged on its own.
    private static bool IsPrivateEmbeddedIPv4(byte[] bytes) =>
        bytes.AsSpan(..12).IndexOfAnyExcept((byte)0) < 0
        || (bytes.AsSpan(..12).SequenceEqual(nat64Prefix) && IsPrivateIPv4(bytes[12..]))
        || (bytes[0] == 0x20 && bytes[1] == 0x02 && IsPrivateIPv4(bytes[2..6]));

    #endregion
}
