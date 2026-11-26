namespace Drover.Server.Tests;

/// <summary>A device-code model sign-in over the real server: the client reads progress until the person approves.</summary>
public sealed class ModelSignInPollEndpointTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly FakeModelSignIn poll = new(ScriptedModelProvider.ConnectionKind)
    {
        Wait = ModelSignInWait.Poll,
        UserCode = "WXYZ-1234",
    };

    #endregion

    #region Signing In

    [Fact]
    public async Task A_member_starts_a_poll_with_no_return_address_and_sees_the_user_code()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);

        var response = await client.Post($"{Path(owner.SpaceId)}/sign-in", new StartModelSignInRequest(poll.Kind, null));
        var started = (await response.Content.ReadFromJsonAsync<ModelSignInStarted>(Token))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        started.Wait.Should().Be(ModelSignInWaits.Poll);
        started.UserCode.Should().Be("WXYZ-1234");
        started.Address.Should().StartWith("https://");
        started.AttemptId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Reading_progress_stays_waiting_until_finish_after_the_person_approves()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);
        var started = await Start(client, owner.SpaceId);
        var waiting = await client.Get($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}");
        poll.DeviceApproved = true;
        var finish = await client.Post($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}/finish", new FinishModelSignInRequest(string.Empty));
        var done = await client.Get($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}");

        (await waiting.Content.ReadFromJsonAsync<ModelSignInProgress>(Token))!.Status.Should().Be(ModelSignInStatuses.Waiting);
        finish.StatusCode.Should().Be(HttpStatusCode.OK, await finish.Content.ReadAsStringAsync(Token));
        var summary = (await finish.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
        summary.SignIn.Should().Be(new ModelSignInSummary(ModelSignInStatuses.Active, FakeModelSignIn.AccountLabel));
        (await done.Content.ReadFromJsonAsync<ModelSignInProgress>(Token))!.Status.Should().Be(ModelSignInStatuses.Done);
        (await finish.Content.ReadAsStringAsync(Token)).Should().NotContainAny(poll.Issued);
    }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<IModelSignIn>(poll);
    }

    #endregion

    #region Private Methods

    private static string Path(Guid spaceId) => $"/api/spaces/{spaceId}/model-connections";

    private async Task<ApiClient> SignedIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<ModelSignInStarted> Start(ApiClient client, Guid spaceId)
    {
        var response = await client.Post($"{Path(spaceId)}/sign-in", new StartModelSignInRequest(poll.Kind, null));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ModelSignInStarted>(Token))!;
    }

    #endregion
}
