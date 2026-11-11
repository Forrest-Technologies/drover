namespace Drover.Engine.Tests;

public sealed class PrivateAddressesTests
{
    #region Public Methods

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:a00:1::1")]
    [InlineData("ff02::1")]
    public void A_private_loopback_link_local_or_wrapped_private_address_is_refused(string text)
    {
        PrivateAddresses.Contains(IPAddress.Parse(text)).Should().BeTrue();
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    public void A_public_address_is_not_refused(string text)
    {
        PrivateAddresses.Contains(IPAddress.Parse(text)).Should().BeFalse();
    }

    #endregion
}
