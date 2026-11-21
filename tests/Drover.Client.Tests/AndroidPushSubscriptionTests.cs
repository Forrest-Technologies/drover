namespace Drover.Client.Tests;

/// <summary>
/// What an Android device keeps of its notification registration. The address the distributor gave and the address the
/// server was last told are kept apart, so an address the server has not heard of is known, and a record kept before
/// that was told apart still reads.
/// </summary>
public sealed class AndroidPushSubscriptionTests
{
    #region Public Methods

    [Fact]
    public void A_new_registration_has_no_address_and_nothing_to_report()
    {
        var saved = AndroidPushSubscription.Create("org.sample.distributor", Guid.NewGuid());

        (saved.Endpoint, saved.Reported, saved.HasUnreportedEndpoint).Should().Be((null, null, false));
    }

    [Fact]
    public void A_rotated_address_the_server_has_not_been_told_is_unreported_until_it_is_reported()
    {
        var saved = AndroidPushSubscription.Create("org.sample.distributor", Guid.NewGuid()) with
        {
            Endpoint = "https://push.example/new",
            Reported = "https://push.example/old",
        };

        saved.HasUnreportedEndpoint.Should().BeTrue();
        (saved with { Reported = saved.Endpoint }).HasUnreportedEndpoint.Should().BeFalse();
    }

    [Fact]
    public void What_is_stored_reads_back_with_both_addresses()
    {
        var saved = AndroidPushSubscription.Create("org.sample.distributor", Guid.NewGuid()) with
        {
            Endpoint = "https://push.example/new",
            Reported = "https://push.example/old",
        };

        var read = AndroidPushSubscription.Read(saved.Store());

        read.Should().Be(saved);
        read!.Request().Should().Be(new PushDeviceRequest("https://push.example/new", saved.PublicKey, saved.AuthSecret));
    }

    [Fact]
    public void A_registration_with_no_address_yet_reads_back_with_none()
    {
        var saved = AndroidPushSubscription.Create("org.sample.distributor", Guid.NewGuid());

        AndroidPushSubscription.Read(saved.Store()).Should().Be(saved);
    }

    [Fact]
    public void A_record_kept_before_the_reported_address_was_remembered_counts_as_reported()
    {
        var saved = AndroidPushSubscription.Create("org.sample.distributor", Guid.NewGuid()) with { Endpoint = "https://push.example/kept" };
        var legacy = string.Join('\n', saved.Store().Split('\n')[..7]);

        var read = AndroidPushSubscription.Read(legacy);

        read.Should().NotBeNull();
        (read!.Endpoint, read.Reported, read.HasUnreportedEndpoint).Should().Be(("https://push.example/kept", "https://push.example/kept", false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not\na\nrecord")]
    public void A_record_that_is_not_a_registration_reads_as_none(string stored)
    {
        AndroidPushSubscription.Read(stored).Should().BeNull();
        AndroidPushSubscription.Read(null).Should().BeNull();
    }

    #endregion
}
