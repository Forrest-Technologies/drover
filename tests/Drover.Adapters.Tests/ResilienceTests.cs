namespace Drover.Adapters.Tests;

/// <summary>The outbound resilience each adapter registers with its HttpClient: retries, rate-limit waits, timeouts.</summary>
public sealed class ResilienceTests
{
    #region Venice

    [Fact]
    public async Task Venice_retries_a_transient_failure()
    {
        var (client, handler) = BuildVenice("unavailable_then_ok");

        var response = await client.Ask("Say hello.");

        response.Text.Should().Be("Hello after a retry.");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Venice_waits_for_the_rate_limit_reset_before_retrying()
    {
        var (client, handler) = BuildVenice("rate_limited_then_ok");
        var started = DateTimeOffset.UtcNow;

        var response = await client.Ask("Say hello.");

        response.Text.Should().Be("Hello after waiting.");
        handler.Requests.Should().HaveCount(2);
        (DateTimeOffset.UtcNow - started).Should().BeGreaterThan(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Venice_reports_a_long_rate_limit_as_retry_later_instead_of_waiting_it_out()
    {
        var (client, handler) = BuildVenice(ConformanceScenarios.RateLimited);

        var failure = await FluentActions.Awaiting(() => client.Ask("Say hello.")).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.RetryLater);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Venice_does_not_trip_the_shared_circuit_for_one_keys_rate_limit()
    {
        var (client, handler) = BuildVenice(ConformanceScenarios.RateLimited);

        for (var attempt = 0; attempt < 40; attempt++)
        {
            await FluentActions.Awaiting(() => client.Ask("Say hello.")).Should().ThrowAsync<ModelProviderException>()
                .Where(failure => failure.Failure == ModelFailure.RetryLater);
        }

        handler.Requests.Should().HaveCount(40);
    }

    #endregion

    #region Compatible

    [Fact]
    public async Task The_compatible_adapter_retries_a_server_error_and_then_gives_up()
    {
        var handler = new RecordedHandler(RecordedFixture.Load("openai-compatible", "server_error"));
        var services = new ServiceCollection();
        services.AddOpenAICompatibleProvider();
        services.AddHttpClient(OpenAICompatibleProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.Configure<HttpStandardResilienceOptions>(
            $"{OpenAICompatibleProvider.HttpClientName}-standard",
            options => options.Retry.Delay = TimeSpan.Zero);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IModelClientProvider>().CreateChatClient(
            new ModelConnection(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address),
            TestKeys.Generate(),
            CompatibleRig.ModelId,
            new ModelSettings());

        var failure = await FluentActions.Awaiting(() => client.Ask("Say hello.")).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.Unreachable);
        handler.Requests.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(OpenAICompatibleProvider.HttpClientName)]
    [InlineData(VeniceProvider.HttpClientName)]
    public void Both_adapters_allow_a_reasoning_model_minutes_to_answer(string clientName)
    {
        var services = new ServiceCollection();
        services.AddOpenAICompatibleProvider();
        services.AddVeniceProvider();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get($"{clientName}-standard");

        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromMinutes(2));
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromMinutes(6));
    }

    #endregion

    #region Helpers

    private static (IChatClient Client, RecordedHandler Handler) BuildVenice(string fixtureName)
    {
        var handler = new RecordedHandler(RecordedFixture.Load("venice", fixtureName));
        var services = new ServiceCollection();
        services.AddVeniceProvider();
        services.AddHttpClient(VeniceProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.Configure<HttpStandardResilienceOptions>(
            $"{VeniceProvider.HttpClientName}-standard",
            options => options.Retry.Delay = TimeSpan.Zero);

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IModelClientProvider>().CreateChatClient(
            new ModelConnection(VeniceProvider.ConnectionKind, VeniceProvider.BaseAddress),
            TestKeys.Generate(),
            VeniceRig.ModelId,
            new ModelSettings());

        return (client, handler);
    }

    #endregion
}
