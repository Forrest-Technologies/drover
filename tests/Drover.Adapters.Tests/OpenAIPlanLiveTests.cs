namespace Drover.Adapters.Tests;

/// <summary>
/// Runs the ChatGPT plan adapter against the real provider with an access token from the owner's own plan, signed in
/// through Drover's desktop flow, in DROVER_TEST_OPENAI_PLAN_TOKEN and a model the plan lists in
/// DROVER_TEST_OPENAI_PLAN_MODEL (no model name is written in code). These tests are explicit and carry the Live
/// trait: the normal run, and pull request CI, never execute them. Run them with:
/// <c>DROVER_TEST_OPENAI_PLAN_TOKEN=... DROVER_TEST_OPENAI_PLAN_MODEL=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-class "*OpenAIPlanLiveTests"</c>.
/// They answer what the fixtures cannot: that the plan takes the shaped request, tools in a namespace, and a function
/// call sent back with its namespace, and that a completed stream carries token usage.
/// </summary>
[Trait("Live", "true")]
public sealed class OpenAIPlanLiveTests
{
    #region Private Fields

    private const string TokenVariable = "DROVER_TEST_OPENAI_PLAN_TOKEN";

    private const string ModelVariable = "DROVER_TEST_OPENAI_PLAN_MODEL";

    private static readonly OpenAIPlanOptions settings = new();

    private static readonly ModelEndpoint connection = new(OpenAIPlanProvider.ConnectionKind, settings.ApiAddress);

    #endregion

    #region Tests

    [Fact(Explicit = true)]
    public async Task Lists_the_plans_models_and_includes_the_one_named()
    {
        var models = await Provider().ListModels(connection, Read(TokenVariable), Token);

        models.Select(model => model.Id).Should().Contain(Read(ModelVariable));
    }

    [Fact(Explicit = true)]
    public async Task Answers_with_instructions_and_reports_usage()
    {
        var client = Provider().CreateChatClient(connection, Read(TokenVariable), Read(ModelVariable), new ModelSettings { Temperature = 0.2, MaxOutputTokens = 100 });
        var request = new ModelChatRequest(
            [new ModelChatMessage("system", [new ModelText("Answer in one word.")]), new ModelChatMessage("user", [new ModelText("Say hello.")])],
            new ModelChatOptions { Instructions = "You are a test." });

        var response = await client.GetResponse(request, Token);

        response.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Should().NotBeEmpty();
        response.Usage.Should().NotBeNull("a completed stream carries the token counts the usage page shows");
    }

    [Fact(Explicit = true)]
    public async Task Completes_a_tool_call_round_trip_through_the_namespace()
    {
        var client = Provider().CreateChatClient(connection, Read(TokenVariable), Read(ModelVariable), new ModelSettings());
        var tool = new ModelTool("get_weather", "Weather for a city", JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }));
        var options = new ModelChatOptions { Tools = [tool], Instructions = "Use the tool to answer." };
        List<ModelChatMessage> conversation = [new("user", [new ModelText("What is the weather in Paris?")])];

        var first = await client.GetResponse(new ModelChatRequest(conversation, options), Token);
        var call = first.Messages.SelectMany(message => message.Contents).OfType<ModelToolCall>().Single();
        conversation.AddRange(first.Messages);
        conversation.Add(new ModelChatMessage("tool", [new ModelToolResult(call.CallId, JsonSerializer.SerializeToElement("Sunny, 21 degrees"))]));
        var second = await client.GetResponse(new ModelChatRequest(conversation, options), Token);

        call.Name.Should().Be("get_weather");
        second.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Should().NotBeEmpty();
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OpenAIPlanProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddOpenAIPlanProvider();
        return services.BuildServiceProvider().GetRequiredService<OpenAIPlanProvider>();
    }

    private static string Read(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Set {variable} to run this live test.");

    #endregion
}
