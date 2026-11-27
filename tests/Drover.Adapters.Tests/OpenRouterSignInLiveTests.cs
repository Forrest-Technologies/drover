namespace Drover.Adapters.Tests;

/// <summary>
/// Signs in to the real OpenRouter once, as the owner, and runs the key the exchange made. The test listens on a free
/// loopback port, writes the sign-in address to the test output, and waits up to five minutes for the owner to open
/// it and approve; no account details are needed beforehand. A model name is read from DROVER_TEST_OPENROUTER_MODEL
/// (none is written in code); without it the test stops after the model list. It carries the Live trait: the normal run
/// and pull request CI never execute it. Run it with:
/// <c>DROVER_TEST_OPENROUTER_MODEL=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-class "*OpenRouterSignInLiveTests"</c>.
/// It makes one short request. It also settles the two values the code ships unconfirmed: that <c>localhost</c> and not
/// <c>127.0.0.1</c> is the callback host the provider takes, and that <c>state</c> comes back unchanged.
/// </summary>
[Trait("Live", "true")]
public sealed class OpenRouterSignInLiveTests
{
    #region Private Fields

    private const string ModelVariable = "DROVER_TEST_OPENROUTER_MODEL";

    #endregion

    #region Sign-In

    [Fact(Explicit = true)]
    public async Task The_owner_signs_in_and_the_key_it_makes_lists_models_and_answers()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var settings = new ChangingOptions<OpenRouterSignInOptions>(new OpenRouterSignInOptions());
        var clients = new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var signIn = new OpenRouterSignIn(clients, settings);
        var provider = new OpenRouterProvider(clients, settings);
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/auth/callback/");
        listener.Start();
        var attempt = new ModelSignInAttempt(
            Guid.NewGuid(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            new Uri($"http://localhost:{port}/auth/callback"));

        var step = await signIn.Begin(attempt, cancellation);
        TestContext.Current.SendDiagnosticMessage($"Open this address and approve: {step.Address}");
        var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(5), cancellation);
        context.Response.StatusCode = 200;
        await using (var body = context.Response.OutputStream)
        {
            await body.WriteAsync("Signed in. You can close this tab."u8.ToArray(), cancellation);
        }

        var tokens = await signIn.Finish(attempt, new ModelSignInReturn(context.Request.Url!.Query), cancellation);

        tokens.Should().NotBeNull();
        tokens!.ExpiresAt.Should().BeNull();
        var connection = new ModelEndpoint(OpenRouterProvider.ConnectionKind, settings.CurrentValue.ApiAddress);
        (await provider.ListModels(connection, tokens.AccessToken, cancellation)).Should().NotBeEmpty();

        if (Environment.GetEnvironmentVariable(ModelVariable) is { Length: > 0 } model)
        {
            var reply = await provider.CreateChatClient(connection, tokens.AccessToken, model, new ModelSettings()).Ask("Say hello.");
            reply.Text.Should().NotBeNullOrWhiteSpace();
        }
    }

    #endregion

    #region Private Methods

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    #endregion
}
