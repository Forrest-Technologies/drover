namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// Connecting a model with a person's own plan: choose the provider, approve in the browser, done. Every state of the
/// connect screen and of a sign-in connection on Models has its one sentence and at most one action, against a fake
/// server and a fake listener. The provider here is a sample; the real names come from the server.
/// </summary>
public sealed class ModelSignInFlowTests : IDisposable
{
    #region Private Fields

    private const string KindsPath = "/api/model-provider-kinds";

    private static readonly Guid spaceId = Samples.Session().Spaces[0].Id;

    private static readonly string space = $"/api/spaces/{spaceId}/model-connections";

    private static readonly Guid attemptId = Guid.NewGuid();

    private static readonly ModelConnectionSummary signedIn = new(
        Guid.NewGuid(), "Sample plan", "sample_plan_signin", null, false, "ready", null, [], null, null,
        new ModelSignInSummary(ModelSignInStatuses.Active, "sam@example.test"));

    private readonly FakeServer server = new();

    private readonly FakeModelSignInListener listener = new();

    private readonly SessionState session;

    private readonly List<string> opened = [];

    #endregion

    #region Constructors

    public ModelSignInFlowTests()
    {
        session = new(server.CreateApi(), new ShareInbox());
        server.Answer("GET", "/api/session", Samples.Session());
        server.Answer("GET", KindsPath, WithPlan());
        server.Answer("POST", $"{space}/sign-in", new ModelSignInStarted(attemptId, "https://auth.example.test/authorize?state=sample", ModelSignInWaits.Return));
        server.Answer("POST", $"{space}/sign-in/{attemptId}/finish", signedIn);
    }

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task A_sign_in_that_uses_an_account_and_not_a_plan_says_account_in_every_line()
    {
        var kind = new ModelProviderKindResponse("sample_account_signin", "Sample account", "required", "fixed", null, null, false, [], SignIn: true, SignInName: "Sample", PersonalPlan: false);
        server.Answer("POST", $"{space}/sign-in", new ModelSignInStarted(attemptId, "https://auth.example.test/authorize?state=sample", ModelSignInWaits.Return));
        server.Answer("POST", $"{space}/sign-in/{attemptId}/finish", signedIn with { Kind = kind.Kind });
        var flow = new ModelSignInFlow(server.CreateApi(), listener, NullLogger.Instance);

        flow.Show(kind);
        var ready = flow.PlanLine;
        var running = flow.Start(spaceId, Open);
        await Until(() => opened.Count == 1);
        listener.Current!.Bring("code=sample-code&state=sample");
        await running;

        ready.Should().Be("Uses your own Sample account. Drover does not charge for it.");
        flow.Sentence.Should().Be("Signed in as sam@example.test. Your bots can use your Sample account.");
    }

    [Fact]
    public async Task Choosing_a_plan_shows_one_button_and_one_line_and_no_key_or_address()
    {
        var model = await Chosen();

        model.Named.Options.Select(option => option.Label).Should().Contain("Sample plan");
        model.ShowsSignIn.Should().BeTrue();
        model.Named.ShowsKey.Should().BeFalse();
        model.Named.ShowsAddress.Should().BeFalse();
        model.SignIn.Stage.Should().Be(ModelSignInStage.Ready);
        model.SignIn.ActionWords.Should().Be("Continue with Sample");
        model.SignIn.PlanLine.Should().Be("Uses your own Sample plan. Drover does not charge for it.");
        model.SignIn.Sentence.Should().BeNull();
    }

    [Fact]
    public async Task Approving_in_the_browser_ends_signed_in_with_the_persons_own_account_and_no_token_on_the_device()
    {
        var model = await Chosen();

        var running = model.ActOnSignIn(Open);
        model.SignIn.Stage.Should().Be(ModelSignInStage.Waiting);
        model.SignIn.Sentence.Should().Be("Finish signing in in your browser.");
        model.SignIn.ActionWords.Should().Be("Cancel");
        await Until(() => opened.Count == 1);
        listener.Current!.Bring("code=sample-code&state=sample");
        await running;

        opened.Should().Equal("https://auth.example.test/authorize?state=sample");
        JsonSerializer.Deserialize<StartModelSignInRequest>(server.Sent("POST", $"{space}/sign-in").Single(), JsonSerializerOptions.Web)
            .Should().Be(new StartModelSignInRequest("sample_plan_signin", FakeModelSignInListener.RedirectUri.AbsoluteUri));
        JsonSerializer.Deserialize<FinishModelSignInRequest>(server.Sent("POST", $"{space}/sign-in/{attemptId}/finish").Single(), JsonSerializerOptions.Web)!
            .Query.Should().Be("code=sample-code&state=sample");
        model.SignIn.Stage.Should().Be(ModelSignInStage.Done);
        model.SignIn.Sentence.Should().Be("Signed in as sam@example.test. Your bots can use your Sample plan.");
        model.SignIn.ActionWords.Should().BeNull();
        model.Passed.Should().BeTrue("the step can go on once the person is signed in");
        model.Connection.Should().BeEquivalentTo(signedIn);
        listener.Current.Disposed.Should().BeTrue("the listener closes once the return is handed on");
    }

    [Fact]
    public async Task Cancelling_ends_with_its_sentence_and_the_button_starts_again()
    {
        var model = await Chosen();

        var running = model.ActOnSignIn(Open);
        await Until(() => opened.Count == 1);
        await model.ActOnSignIn(Open);
        await running;

        model.SignIn.Stage.Should().Be(ModelSignInStage.Ended);
        model.SignIn.Sentence.Should().Be("Sign-in was cancelled.");
        model.SignIn.ActionWords.Should().Be("Continue with Sample");
        server.Sent("POST", $"{space}/sign-in/{attemptId}/finish").Should().BeEmpty();
    }

    [Fact]
    public async Task A_sign_in_that_takes_over_ten_minutes_says_so()
    {
        var time = new FakeTimeProvider();
        var flow = new ModelSignInFlow(server.CreateApi(), listener, NullLogger.Instance, time);
        flow.Show(WithPlan().Single(kind => kind.SignIn));

        var running = flow.Start(spaceId, Open);
        await Until(() => opened.Count == 1);
        time.Advance(ModelSignInFlow.Patience);
        await running;

        flow.Sentence.Should().Be("Sign-in took too long.");
        flow.ActionWords.Should().Be("Continue with Sample");
    }

    [Fact]
    public async Task A_refusal_the_server_explains_shows_its_sentence_with_the_button()
    {
        var notAllowed = "ChatGPT signed you in but did not allow plan use. Try again and allow it.";
        server.Refuse("POST", $"{space}/sign-in/{attemptId}/finish", HttpStatusCode.BadRequest, notAllowed);
        var model = await Chosen();

        var running = model.ActOnSignIn(Open);
        await Until(() => opened.Count == 1);
        listener.Current!.Bring("code=sample-code&state=sample");
        await running;

        model.SignIn.Stage.Should().Be(ModelSignInStage.Ended);
        model.SignIn.Sentence.Should().Be(notAllowed);
        model.SignIn.ActionWords.Should().Be("Continue with Sample");
    }

    [Fact]
    public async Task A_kind_this_server_does_not_offer_says_so_and_offers_a_key_instead()
    {
        var notAvailable = "Signing in with a personal plan is not available on this server. You can add an API key instead.";
        server.Refuse("POST", $"{space}/sign-in", HttpStatusCode.Forbidden, notAvailable);
        var model = await Chosen();

        await model.ActOnSignIn(Open);

        model.SignIn.Stage.Should().Be(ModelSignInStage.NotAvailable);
        model.SignIn.Sentence.Should().Be(notAvailable);
        model.SignIn.ActionWords.Should().Be("Add a key");
        opened.Should().BeEmpty();
        await model.ActOnSignIn(Open);
        model.Named.Choice.Should().Be("venice", "\"Add a key\" moves to the first provider the server offers that takes one");
        model.Named.ShowsKey.Should().BeTrue();
    }

    [Fact]
    public async Task A_device_that_could_not_listen_now_says_what_to_do_and_tries_again()
    {
        listener.Fails = true;
        var model = await Chosen();

        await model.ActOnSignIn(Open);

        model.SignIn.Stage.Should().Be(ModelSignInStage.CannotListen);
        model.SignIn.Sentence.Should().Be("Drover could not listen for the sign-in on this device. Close other apps that are signing in and try again.");
        model.SignIn.ActionWords.Should().Be("Try again");
        server.Sent("POST", $"{space}/sign-in").Should().BeEmpty();
    }

    [Fact]
    public async Task A_head_that_cannot_listen_needs_the_desktop_app_for_now_and_offers_a_key()
    {
        listener.CanListen = false;
        var model = await Chosen();

        model.SignIn.Stage.Should().Be(ModelSignInStage.CannotListen);
        model.SignIn.Sentence.Should().Be("Signing in with a Sample plan needs the Drover desktop app for now. You can add an API key here.");
        model.SignIn.ActionWords.Should().Be("Add a key");
    }

    [Fact]
    public async Task A_sign_in_connection_on_models_says_the_callers_own_state_and_its_one_action()
    {
        var ended = signedIn with { Id = Guid.NewGuid(), SignIn = new(ModelSignInStatuses.NeedsSignIn, "sam@example.test") };
        var never = signedIn with { Id = Guid.NewGuid(), SignIn = new(ModelSignInStatuses.None, null) };
        var keyed = signedIn with { Id = Guid.NewGuid(), Kind = "venice", SignIn = null };
        var signedOut = signedIn with { Id = Guid.NewGuid(), SignIn = new(ModelSignInStatuses.SignedOut, null) };
        server.Answer("GET", space, (ModelConnectionSummary[])[signedIn, ended, never, keyed, signedOut]);
        var model = Models();

        await model.Open();

        model.SignInFacts(signedIn).Should().Be("Using Sample plan · sam@example.test");
        model.SignInActionWords(signedIn).Should().BeNull();
        model.SignInKindOf(signedIn)!.UsagePage.Should().Be("https://usage.example.test/");
        model.SignInFacts(ended).Should().Be("Your Sample sign-in has ended.");
        model.SignInActionWords(ended).Should().Be("Sign in again");
        model.SignInFacts(never).Should().Be("Sign in with your own Sample plan to use it.");
        model.SignInActionWords(never).Should().Be("Continue with Sample");
        model.SignInFacts(keyed).Should().BeNull("a key connection keeps its own facts");
        model.SignInFacts(signedOut).Should().Be("Signed out of your Sample plan.");
        model.SignInActionWords(signedOut).Should().Be("Continue with Sample");
    }

    [Fact]
    public async Task Sign_in_again_starts_the_flow_at_once_from_the_card()
    {
        var ended = signedIn with { SignIn = new(ModelSignInStatuses.NeedsSignIn, "sam@example.test") };
        server.AnswerInTurn("GET", space, (ModelConnectionSummary[])[ended], (ModelConnectionSummary[])[signedIn]);
        var model = Models();
        await model.Open();

        var running = model.SignInAgain(ended, Open);
        await Until(() => opened.Count == 1);
        model.SigningInTo.Should().Be(ended.Id);
        listener.Current!.Bring("code=sample-code&state=sample");
        await running;

        model.SignIn.Stage.Should().Be(ModelSignInStage.Done);
        model.SigningInTo.Should().BeNull();
        model.Connections.Should().ContainSingle().Which.SignIn!.Status.Should().Be(ModelSignInStatuses.Active);
    }

    [Fact]
    public async Task Signing_signedOutasks_nothing_and_removes_the_callers_own_sign_in()
    {
        server.Answer("GET", space, (ModelConnectionSummary[])[signedIn]);
        server.Answer("DELETE", $"{space}/{signedIn.Id}/sign-in", HttpStatusCode.NoContent);
        var model = Models();
        await model.Open();

        await model.SignOut(signedIn);

        server.Requests.Should().Contain(request => request.Method == "DELETE" && request.Path == $"{space}/{signedIn.Id}/sign-in");
        model.Notice.Should().Be("Signed out of Sample plan.");
    }

    #endregion

    #region Private Methods

    private async Task<ConnectModelViewModel> Chosen()
    {
        var model = new ConnectModelViewModel(server.CreateApi(), session, new Navigator(), listener, NullLogger<ConnectModelViewModel>.Instance);
        await model.Open();
        model.ChooseProvider("sample_plan_signin");
        return model;
    }

    private ModelConnectionsViewModel Models() =>
        new(server.CreateApi(), session, new(server.CreateApi(), session, NullLogger<SpaceVoice>.Instance), listener, NullLogger<ModelConnectionsViewModel>.Instance);

    private Task Open(string address)
    {
        opened.Add(address);
        return Task.CompletedTask;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var tries = 0; tries < 200 && !condition(); tries++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        condition().Should().BeTrue();
    }

    /// <summary>The kinds a server offers with a sample sign-in kind beside a key kind with its own wire.</summary>
    private static ModelProviderKindResponse[] WithPlan() =>
    [
        .. SampleProviders.WithOwnWire(),
        new("sample_plan_signin", "Sample plan", "required", "fixed", null, null, false, [], SignIn: true, SignInName: "Sample", UsagePage: "https://usage.example.test/", PersonalPlan: true),
    ];

    #endregion
}
