namespace Drover.Adapters.Tests;

/// <summary>
/// The outbound resilience each adapter registers with its HttpClient. A model call is billed when the provider
/// receives it, so a POST is repeated only on a rate limit or a connection that never reached the server.
/// </summary>
public sealed class ResilienceTests
{
    #region Constants

    private const string Compatible = "compatible";

    private const string Venice = "venice";

    #endregion

    #region Calls That Are Billed

    [Theory]
    [InlineData(Compatible)]
    [InlineData(Venice)]
    public async Task Does_not_send_a_model_call_again_after_a_server_error(string adapter)
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}")));
        var client = Build(adapter, handler).Client;

        var failure = await Failing(client);

        failure.Failure.Should().Be(ModelFailure.Unreachable);
        handler.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(Compatible)]
    [InlineData(Venice)]
    public async Task Does_not_send_a_model_call_again_after_a_timeout(string adapter)
    {
        var handler = new ScriptedHandler(async (_, _, cancellation) =>
        {
            await Task.Delay(Timeout.Infinite, cancellation);

            return new HttpResponseMessage();
        });
        var client = Build(adapter, handler, options => options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(150)).Client;

        var failure = await Failing(client);

        failure.Failure.Should().Be(ModelFailure.Unreachable);
        failure.Message.Should().Contain("too long");
        failure.InnerException.Should().BeNull();
        handler.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(Compatible)]
    [InlineData(Venice)]
    public async Task Sends_a_model_call_again_after_a_rate_limit(string adapter)
    {
        var handler = new ScriptedHandler((_, attempt, _) => Task.FromResult(attempt == 1
            ? RateLimited()
            : ScriptedHandler.Json(HttpStatusCode.OK, ScriptedHandler.ChatReply)));
        var client = Build(adapter, handler).Client;

        var response = await client.Ask("Say hello.");

        response.Text.Should().Be("ok");
        handler.Count.Should().Be(2);
    }

    [Theory]
    [InlineData(Compatible)]
    [InlineData(Venice)]
    public async Task Sends_a_model_call_again_when_the_connection_never_reached_the_server(string adapter)
    {
        var handler = new ScriptedHandler((_, attempt, _) => attempt == 1
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")
            : Task.FromResult(ScriptedHandler.Json(HttpStatusCode.OK, ScriptedHandler.ChatReply)));
        var client = Build(adapter, handler).Client;

        var response = await client.Ask("Say hello.");

        response.Text.Should().Be("ok");
        handler.Count.Should().Be(2);
    }

    #endregion

    #region Reads

    [Theory]
    [InlineData(Compatible)]
    [InlineData(Venice)]
    public async Task Repeats_a_read_after_a_server_error(string adapter)
    {
        var handler = new ScriptedHandler((request, attempt, _) => Task.FromResult(attempt == 1
            ? ScriptedHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
            : ScriptedHandler.Json(
                HttpStatusCode.OK,
                request.RequestUri!.AbsolutePath.EndsWith("/traits") ? """{"data":{}}""" : ScriptedHandler.ModelList)));
        var rig = Build(adapter, handler);

        var models = await rig.Provider.ListModels(rig.Connection, null, TestContext.Current.CancellationToken);

        models.Select(model => model.Id).Should().Equal("m");
        handler.Count.Should().BeGreaterThan(1);
    }

    #endregion

    #region Venice Rate Limits

    [Fact]
    public async Task Venice_waits_for_the_rate_limit_reset_before_retrying()
    {
        var rig = BuildFromFixture("rate_limited_then_ok");
        var started = DateTimeOffset.UtcNow;

        var response = await rig.Client.Ask("Say hello.");

        response.Text.Should().Be("Hello after waiting.");
        rig.Handler!.Requests.Should().HaveCount(2);
        (DateTimeOffset.UtcNow - started).Should().BeGreaterThan(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Venice_also_waits_for_the_reset_header_seen_on_the_live_service()
    {
        var handler = new ScriptedHandler((_, attempt, _) => Task.FromResult(attempt == 1
            ? RateLimited(resetHeader: "x-ratelimit-resets", resetInMilliseconds: 400)
            : ScriptedHandler.Json(HttpStatusCode.OK, ScriptedHandler.ChatReply)));
        var started = DateTimeOffset.UtcNow;

        await Build(Venice, handler).Client.Ask("Say hello.");

        (DateTimeOffset.UtcNow - started).Should().BeGreaterThan(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Venice_reports_a_long_rate_limit_as_retry_later_instead_of_waiting_it_out()
    {
        var rig = BuildFromFixture(ConformanceScenarios.RateLimited);

        var failure = await Failing(rig.Client);

        failure.Failure.Should().Be(ModelFailure.RetryLater);
        rig.Handler!.Requests.Should().HaveCount(1);
    }

    #endregion

    #region Circuit Breaker

    [Theory]
    [InlineData(Compatible)]
    [InlineData(Venice)]
    public async Task Maps_a_paused_circuit_to_retry_later_without_a_vendor_exception(string adapter)
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}")));
        var client = Build(adapter, handler, options => EagerBreaker(options)).Client;
        ModelProviderException? last = null;

        for (var call = 0; call < 6; call++)
        {
            last = await Failing(client);
        }

        last!.Failure.Should().Be(ModelFailure.RetryLater);
        last.Message.Should().Contain("paused");
        last.InnerException.Should().BeNull();
        handler.Count.Should().BeLessThan(6, "a paused circuit sends nothing");
    }

    [Theory]
    [InlineData(Compatible)]
    [InlineData(Venice)]
    public async Task Does_not_count_rate_limits_toward_the_circuit(string adapter)
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(RateLimited(resetInMilliseconds: 60_000)));
        var client = Build(adapter, handler, options => EagerBreaker(options)).Client;

        for (var call = 0; call < 10; call++)
        {
            var failure = await Failing(client);

            failure.Failure.Should().Be(ModelFailure.RetryLater);
            failure.Message.Should().NotContain("paused");
        }
    }

    [Fact]
    public async Task One_endpoints_failures_do_not_pause_calls_to_another_endpoint()
    {
        var handler = new ScriptedHandler((request, _, _) => Task.FromResult(request.RequestUri!.Host == "broken.example.test"
            ? ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}")
            : ScriptedHandler.Json(HttpStatusCode.OK, ScriptedHandler.ModelList)));
        var rig = Build(Compatible, handler, options => EagerBreaker(options));
        var broken = rig.Connection with { BaseAddress = new Uri("https://broken.example.test/v1") };
        var healthy = rig.Connection with { BaseAddress = new Uri("https://healthy.example.test/v1") };
        ModelProviderException? last = null;

        for (var call = 0; call < 8; call++)
        {
            last = (await FluentActions
                .Awaiting(() => rig.Provider.ListModels(broken, null, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<ModelProviderException>()).Which;
        }

        last!.Message.Should().Contain("paused");
        var models = await rig.Provider.ListModels(healthy, null, TestContext.Current.CancellationToken);
        models.Select(model => model.Id).Should().Equal("m");
    }

    #endregion

    #region Timeouts

    [Theory]
    [InlineData(OpenAICompatibleProvider.HttpClientName)]
    [InlineData(VeniceProvider.HttpClientName)]
    public void Both_adapters_allow_a_reasoning_model_minutes_to_answer(string clientName)
    {
        var services = new ServiceCollection();
        services.AddOpenAICompatibleProvider();
        services.AddOpenAICompatibleProvider();
        services.AddVeniceProvider();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get($"{clientName}-standard");

        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromMinutes(2));
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromMinutes(6));
    }

    #endregion

    #region Helpers

    private sealed record Built(IModelClientProvider Provider, ModelEndpoint Connection, IChatClient Client, RecordedHandler? Handler);

    private static Built BuildFromFixture(string fixtureName)
    {
        var handler = new RecordedHandler(RecordedFixture.Load("venice", fixtureName));

        return Build(Venice, handler) with { Handler = handler };
    }

    private static Built Build(string adapter, HttpMessageHandler handler, Action<HttpStandardResilienceOptions>? tune = null)
    {
        var venice = adapter == Venice;
        var clientName = venice ? VeniceProvider.HttpClientName : OpenAICompatibleProvider.HttpClientName;
        var services = new ServiceCollection();

        if (venice)
        {
            services.AddOpenAICompatibleProvider();
            services.AddVeniceProvider();
        }
        else
        {
            services.AddOpenAICompatibleProvider();
        }

        services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.Configure<HttpStandardResilienceOptions>($"{clientName}-standard", options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
            tune?.Invoke(options);
        });

        var provider = services.BuildServiceProvider().GetRequiredService<IModelClientProvider>();
        var connection = venice
            ? new ModelEndpoint(VeniceProvider.ConnectionKind, VeniceProvider.BaseAddress)
            : new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address);
        var client = provider.CreateChatClient(connection, TestKeys.Generate(), "m", new ModelSettings());

        return new Built(provider, connection, new ModelChatClientBridge(client), handler as RecordedHandler);
    }

    private static void EagerBreaker(HttpStandardResilienceOptions options)
    {
        options.CircuitBreaker.MinimumThroughput = 2;
        options.CircuitBreaker.FailureRatio = 0.5;
    }

    /// <summary>A 429 carrying the reset header Venice documents, or the one seen on the live service.</summary>
    private static HttpResponseMessage RateLimited(string resetHeader = "x-ratelimit-reset-requests", int resetInMilliseconds = 100)
    {
        var response = ScriptedHandler.Json(HttpStatusCode.TooManyRequests, """{"error":"Rate limit exceeded"}""");
        response.Headers.TryAddWithoutValidation(
            resetHeader,
            DateTimeOffset.UtcNow.AddMilliseconds(resetInMilliseconds).ToUnixTimeMilliseconds().ToString());

        return response;
    }

    private static async Task<ModelProviderException> Failing(IChatClient client) =>
        (await FluentActions.Awaiting(() => client.Ask("Say hello.")).Should().ThrowAsync<ModelProviderException>()).Which;

    #endregion
}
