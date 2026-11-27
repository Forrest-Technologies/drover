namespace Drover.Adapters.Tests;

/// <summary>
/// How the provider's errors reach the caller. The shapes are the provider's (https://ai.google.dev/gemini-api/docs/troubleshooting
/// and the third-party reproductions named on <c>GeminiFailures</c>, read 2026-11-27): a wrong key is HTTP 400 with an ErrorInfo
/// reason API_KEY_INVALID, a blocked or missing key is 403, and a 429 names its wait in a RetryInfo detail and sends no header.
/// </summary>
public sealed class GeminiFailureTests
{
    #region Private Fields

    private const string PrivateWords = "the person's private prompt";

    #endregion

    #region Keys

    [Fact]
    public async Task A_wrong_key_is_invalid_on_a_test_and_on_a_run()
    {
        var script = new GeminiScript(WrongKey());

        var result = await script.Provider.TestConnection(GeminiScript.Connection, script.Key, Token);
        var failure = await FluentActions.Awaiting(() => script.Client().GetResponse(Prompt(), Token)).Should().ThrowAsync<ModelProviderException>();

        result.Failure.Should().Be(ModelFailure.InvalidKey);
        result.Message.Should().Contain("key").And.NotContain(script.Key);
        failure.Which.Failure.Should().Be(ModelFailure.InvalidKey);
        failure.Which.Message.Should().NotContain(script.Key).And.NotContain("API key not valid");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_blocked_or_unauthenticated_key_is_invalid(HttpStatusCode status)
    {
        var script = new GeminiScript(Error(status, "PERMISSION_DENIED", "Your API key was reported as leaked."));

        var result = await script.Provider.TestConnection(GeminiScript.Connection, script.Key, Token);
        var failure = await FluentActions.Awaiting(() => script.Client().GetResponse(Prompt(), Token)).Should().ThrowAsync<ModelProviderException>();

        result.Failure.Should().Be(ModelFailure.InvalidKey);
        failure.Which.Failure.Should().Be(ModelFailure.InvalidKey);
    }

    [Fact]
    public async Task A_bad_request_that_is_not_about_the_key_is_a_rejection_and_is_not_called_invalid()
    {
        var script = new GeminiScript(Error(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Unknown name \"x\" at 'generation_config'"));

        var failure = await FluentActions.Awaiting(() => script.Client().GetResponse(Prompt(), Token)).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.Rejected);
        failure.Which.Message.Should().Contain("400");
    }

    #endregion

    #region Limits

    [Fact]
    public async Task A_rate_limit_is_retry_later_with_the_wait_the_body_names()
    {
        var script = new GeminiScript(ScriptedAnswer.Json(new
        {
            error = new
            {
                code = 429,
                message = PrivateWords,
                status = "RESOURCE_EXHAUSTED",
                details = new object[] { new Dictionary<string, string> { ["@type"] = "type.googleapis.com/google.rpc.RetryInfo", ["retryDelay"] = "34.4s" } },
            },
        }, HttpStatusCode.TooManyRequests));

        var failure = await FluentActions.Awaiting(() => script.Client().GetResponse(Prompt(), Token)).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.RetryLater);
        failure.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(34.4));
        failure.Which.Message.Should().NotContain(PrivateWords);
    }

    [Fact]
    public async Task A_rate_limit_with_no_wait_is_retry_later_without_one()
    {
        var script = new GeminiScript(Error(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "Resource has been exhausted (e.g. check quota)."));

        var failure = await FluentActions.Awaiting(() => script.Client().GetResponse(Prompt(), Token)).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.RetryLater);
        failure.Which.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task A_rate_limit_is_not_sent_again_by_the_client_pipeline()
    {
        var services = new ServiceCollection();
        var script = new GeminiScript(Error(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "Slow down."));
        services.AddGeminiProvider();
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => script.Handler));
        using var provider = services.BuildServiceProvider();

        var failure = await FluentActions
            .Awaiting(() => provider.GetRequiredService<GeminiProvider>().CreateChatClient(GeminiScript.Connection, script.Key, GeminiScript.Model, new ModelSettings()).GetResponse(Prompt(), Token))
            .Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.RetryLater);
        script.Handler.Count.Should().Be(1, "the caller decides when to try again, with the provider's wait");
    }

    [Fact]
    public async Task An_empty_prepay_balance_is_out_of_credit()
    {
        var script = new GeminiScript(Error(HttpStatusCode.PaymentRequired, "RESOURCE_EXHAUSTED", "Your prepayment credits are depleted."));

        var failure = await FluentActions.Awaiting(() => script.Client().GetResponse(Prompt(), Token)).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.OutOfCredit);
    }

    [Fact]
    public async Task An_input_longer_than_the_model_reads_is_too_long()
    {
        var script = new GeminiScript(Error(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "The input token count (9) exceeds the maximum number of tokens allowed (5)."));

        var failure = await FluentActions.Awaiting(() => script.Client().GetResponse(Prompt(), Token)).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.TooLong);
    }

    #endregion

    #region Server Faults

    [Fact]
    public async Task A_server_fault_with_a_page_for_a_body_is_unreachable_and_repeats_nothing()
    {
        var script = new GeminiScript(ScriptedAnswer.Raw($"<html>{PrivateWords}</html>", HttpStatusCode.ServiceUnavailable));

        var failure = await FluentActions.Awaiting(() => script.Client().GetResponse(Prompt(), Token)).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.Unreachable);
        failure.Which.Message.Should().NotContain(PrivateWords);
        failure.Which.InnerException.Should().BeNull();
        script.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_call_without_a_key_is_refused_before_anything_is_sent()
    {
        var script = new GeminiScript(WrongKey());

        var failure = await FluentActions
            .Awaiting(() => Task.FromResult(script.Provider.CreateChatClient(GeminiScript.Connection, null, GeminiScript.Model, new ModelSettings())))
            .Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.InvalidKey);
        script.Sent.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelChatRequest Prompt() => new([new ModelChatMessage("user", [new ModelText("Hi.")])], new ModelChatOptions());

    private static ScriptedAnswer Error(HttpStatusCode status, string name, string message) =>
        ScriptedAnswer.Json(new { error = new { code = (int)status, message, status = name } }, status);

    private static ScriptedAnswer WrongKey() => ScriptedAnswer.Json(
        new
        {
            error = new
            {
                code = 400,
                message = "API key not valid. Please pass a valid API key.",
                status = "INVALID_ARGUMENT",
                details = new object[] { new Dictionary<string, string> { ["@type"] = "type.googleapis.com/google.rpc.ErrorInfo", ["reason"] = "API_KEY_INVALID", ["domain"] = "googleapis.com" } },
            },
        },
        HttpStatusCode.BadRequest);

    #endregion
}
