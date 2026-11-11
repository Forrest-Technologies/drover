namespace Drover.Adapters.Tests;

/// <summary>Failures the vendor client and the resilience layer raise leave the adapters in Drover's terms.</summary>
public sealed class FailureMappingTests
{
    #region Mapping

    [Fact]
    public void A_vendor_failure_with_no_response_is_unreachable_not_rejected()
    {
        var noResponse = new ClientResultException("no response arrived");

        ErrorMappingChatClient.TryMap(noResponse, out var compatible).Should().BeTrue();
        VeniceChatClient.TryMap(noResponse, out var venice).Should().BeTrue();

        compatible.Failure.Should().Be(ModelFailure.Unreachable);
        venice.Failure.Should().Be(ModelFailure.Unreachable);
    }

    [Fact]
    public void A_connection_cut_in_the_middle_of_a_reply_is_unreachable()
    {
        var cut = new HttpIOException(HttpRequestError.ResponseEnded, "the response ended prematurely");

        ErrorMappingChatClient.TryMap(cut, out var compatible).Should().BeTrue();
        VeniceChatClient.TryMap(cut, out var venice).Should().BeTrue();

        compatible.Failure.Should().Be(ModelFailure.Unreachable);
        venice.Failure.Should().Be(ModelFailure.Unreachable);
    }

    [Fact]
    public void A_failure_wrapped_by_the_vendor_client_is_still_found()
    {
        var wrapped = new InvalidOperationException("outer", new HttpRequestException("inner"));

        ErrorMappingChatClient.TryMap(wrapped, out var compatible).Should().BeTrue();
        VeniceChatClient.TryMap(wrapped, out var venice).Should().BeTrue();

        compatible.Failure.Should().Be(ModelFailure.Unreachable);
        venice.Failure.Should().Be(ModelFailure.Unreachable);
    }

    [Fact]
    public void A_paused_circuit_and_a_timeout_map_without_keeping_the_resilience_exception()
    {
        foreach (var map in new Func<Exception, ModelProviderException>[] { MapCompatible, MapVenice })
        {
            var paused = map(new BrokenCircuitException());
            var timedOut = map(new TimeoutRejectedException(TimeSpan.FromSeconds(1)));

            paused.Failure.Should().Be(ModelFailure.RetryLater);
            paused.InnerException.Should().BeNull();
            timedOut.Failure.Should().Be(ModelFailure.Unreachable);
            timedOut.InnerException.Should().BeNull();
        }
    }

    [Fact]
    public void Leaves_alone_a_failure_it_does_not_know()
    {
        ErrorMappingChatClient.TryMap(new InvalidOperationException("a bug"), out _).Should().BeFalse();
        VeniceChatClient.TryMap(new InvalidOperationException("a bug"), out _).Should().BeFalse();
    }

    #endregion

    #region Reset Headers

    [Theory]
    [InlineData("x-ratelimit-resets", 10)]
    [InlineData("x-ratelimit-reset-requests", 10)]
    public void Reads_either_reset_header(string name, int seconds)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_794_400_000_000L);
        var value = (1_794_400_000_000L + (seconds * 1000L)).ToString();

        VeniceHeaders.WaitUntilReset(header => header == name ? value : null, now).Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void Prefers_the_header_seen_on_the_live_service_when_both_are_present()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_794_400_000_000L);

        VeniceHeaders.WaitUntilReset(
            header => header == "x-ratelimit-resets" ? "1794400005000" : "1794400099000",
            now).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Reads_no_wait_when_neither_header_is_present()
    {
        VeniceHeaders.WaitUntilReset(_ => null, DateTimeOffset.UtcNow).Should().BeNull();
    }

    #endregion

    #region Helpers

    private static ModelProviderException MapCompatible(Exception exception)
    {
        ErrorMappingChatClient.TryMap(exception, out var mapped).Should().BeTrue();

        return mapped;
    }

    private static ModelProviderException MapVenice(Exception exception)
    {
        VeniceChatClient.TryMap(exception, out var mapped).Should().BeTrue();

        return mapped;
    }

    #endregion
}
