namespace Drover.Domain.Tests;

public sealed class ModelProviderExceptionTests
{
    [Theory]
    [InlineData(401, ModelFailure.InvalidKey)]
    [InlineData(403, ModelFailure.InvalidKey)]
    [InlineData(402, ModelFailure.OutOfCredit)]
    [InlineData(429, ModelFailure.RetryLater)]
    [InlineData(500, ModelFailure.Unreachable)]
    [InlineData(503, ModelFailure.Unreachable)]
    [InlineData(0, ModelFailure.Unreachable)]
    [InlineData(400, ModelFailure.Rejected)]
    [InlineData(404, ModelFailure.Rejected)]
    public void Maps_a_status_to_a_failure(int status, ModelFailure expected)
    {
        ModelProviderException.FromStatus(status).Failure.Should().Be(expected);
    }

    [Fact]
    public void Keeps_the_wait_on_a_rate_limit()
    {
        var failure = ModelProviderException.FromStatus(429, TimeSpan.FromSeconds(12));

        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void Tells_the_owner_what_to_do_about_a_rejected_key()
    {
        ModelProviderException.FromStatus(401).Message.Should().Contain("key").And.Contain("connection");
    }
}
