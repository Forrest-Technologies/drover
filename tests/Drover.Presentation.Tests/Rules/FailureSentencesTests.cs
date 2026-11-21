namespace Drover.Presentation.Tests.Rules;

public sealed class FailureSentencesTests
{
    [Fact]
    public void An_unreachable_request_leaves_the_next_step_to_its_screen() =>
        Failures.Sentence(new HttpRequestException()).Should().Be("Drover could not reach its server.");

    [Fact]
    public void A_screen_without_a_retry_can_say_how_to_check_the_server() =>
        Failures.Sentence(new HttpRequestException(), "Check that the server is running.")
            .Should().Be("Drover could not reach its server. Check that the server is running.");

    [Fact]
    public void A_server_check_is_not_added_to_an_unrelated_failure() =>
        Failures.Sentence(new TaskCanceledException("Timed out", new TimeoutException()), "Check that the server is running.")
            .Should().Be("Drover's server did not answer in time.");
}
