namespace Drover.Adapters.Tests;

/// <summary>
/// Signs in to the owner's own Claude plan and answers on it, against the real provider. These answer what no fixture
/// can: whether the provider takes a <c>state</c> of Drover's own (not the PKCE verifier), whether the model list
/// answers a plan token, and whether a call with the configured headers, the prefix and a tool of Drover's spelling is
/// served. They are explicit and carry the Live trait, so the normal run and pull request CI never execute them.
/// </summary>
/// <remarks>
/// Two steps, because a person has to approve in a browser:
/// <list type="number">
/// <item><c>dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-method "*Starts_a_sign_in_and_prints_the_address"</c>
/// prints the address. Open it, approve, and copy the <c>code#state</c> the provider's page shows.</item>
/// <item><c>DROVER_TEST_CLAUDE_PLAN_PASTED='code#state' DROVER_TEST_CLAUDE_PLAN_MODEL=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-method "*Finishes_the_pasted_sign_in_and_answers_on_the_plan"</c>
/// within ten minutes. No model name is written in code.</item>
/// </list>
/// The first step keeps the attempt (its state and verifier, which are spent by the second) in a file under the
/// temporary folder, readable by the owner only, and the second deletes it. No token is printed or written anywhere.
/// </remarks>
[Trait("Live", "true")]
public sealed class AnthropicPlanLiveTests
{
    #region Private Fields

    private const string PastedVariable = "DROVER_TEST_CLAUDE_PLAN_PASTED";

    private const string ModelVariable = "DROVER_TEST_CLAUDE_PLAN_MODEL";

    private static readonly string attemptFile = Path.Combine(Path.GetTempPath(), "drover-claude-plan-live-attempt.json");

    private static readonly ModelEndpoint connection = new(AnthropicPlanSignIn.ConnectionKind, AnthropicProvider.BaseAddress);

    #endregion

    #region Sign-in

    [Fact(Explicit = true)]
    public async Task Starts_a_sign_in_and_prints_the_address()
    {
        var attempt = new ModelSignInAttempt(Guid.NewGuid(), Random(), Random(), Random(), null);
        await File.WriteAllTextAsync(attemptFile, JsonSerializer.Serialize(new[] { attempt.State, attempt.Verifier }), TestContext.Current.CancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(attemptFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var step = await Build().SignIn.Begin(attempt, TestContext.Current.CancellationToken);

        step.Wait.Should().Be(ModelSignInWait.Paste);
        Note($"Open this address, approve, and copy what the page shows: {step.Address}");
    }

    [Fact(Explicit = true)]
    public async Task Finishes_the_pasted_sign_in_and_answers_on_the_plan()
    {
        var saved = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(attemptFile, TestContext.Current.CancellationToken))!;
        File.Delete(attemptFile);
        var attempt = new ModelSignInAttempt(Guid.NewGuid(), saved[0], saved[1], Random(), null);
        var (signIn, provider) = Build();

        var tokens = await signIn.Finish(attempt, new ModelSignInReturn(Read(PastedVariable)), TestContext.Current.CancellationToken);
        var renewed = await signIn.Refresh(tokens!, TestContext.Current.CancellationToken);
        var models = await provider.ListModels(connection, renewed.AccessToken, TestContext.Current.CancellationToken);
        var tool = new ModelTool(
            "get_weather",
            "Weather for a city",
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }));
        var request = new ModelChatRequest(
            [new ModelChatMessage("user", [new ModelText("Say hello in three words.")])],
            new ModelChatOptions { Instructions = "You answer briefly.", Tools = [tool] });
        var reply = await provider.CreateChatClient(connection, renewed.AccessToken, Read(ModelVariable), new ModelSettings { MaxOutputTokens = 1000 })
            .GetResponse(request, TestContext.Current.CancellationToken);

        renewed.AccessToken.Should().NotBe(tokens!.AccessToken);
        renewed.RefreshToken.Should().NotBe(tokens.RefreshToken, "refresh tokens rotate");
        models.Should().Contain(model => model.Id == Read(ModelVariable));
        reply.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Should().NotBeEmpty();
        Note($"Signed in; scopes {string.Join(' ', renewed.Scopes)}; expires {renewed.ExpiresAt:O}; {models.Count} models listed.");
    }

    #endregion

    #region Helpers

    private static (AnthropicPlanSignIn SignIn, AnthropicPlanProvider Provider) Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddAnthropicProvider();
        services.AddAnthropicPlan();
        // Kept for the test's life: the clients it hands out are used after this returns.
        var provider = services.BuildServiceProvider();

        return ((AnthropicPlanSignIn)provider.GetServices<IModelSignIn>().Single(), provider.GetRequiredService<AnthropicPlanProvider>());
    }

    private static string Read(string variable) =>
        Environment.GetEnvironmentVariable(variable) ?? throw new InvalidOperationException($"Set {variable} to run this live test.");

    private static string Random() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion
}
