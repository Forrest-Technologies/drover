namespace Drover.Adapters.Tests;

/// <summary>
/// Copilot's device-code sign-in against a scripted GitHub, covering the poll suite plus pending, slow-down,
/// expiry, and a malformed verification address.
/// </summary>
public sealed class CopilotPlanSignInTests : ModelSignInPollConformance
{
    #region Properties

    protected override IReadOnlyList<string> EndedAnswers { get; } = ["401", "403", "chat_disabled"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Device Flow

    [Fact]
    public void A_copilot_plan_is_a_personal_plan() =>
        new CopilotAuthServer().SignIn().IsPersonalPlan.Should().BeTrue();

    [Fact]
    public async Task Begin_posts_the_public_client_id_and_read_user_scope()
    {
        var github = new CopilotAuthServer();
        var signIn = github.SignIn();

        await signIn.Begin(Attempt(), Token);

        var started = github.Requests.Should().ContainSingle(request => request.Uri.AbsolutePath == "/login/device/code").Subject;
        started.Method.Should().Be(HttpMethod.Post);
        started.Body.Should().Contain("client_id=").And.Contain("scope=read%3Auser");
        started.Headers.Should().ContainKey("User-Agent");
        started.Headers.Keys.Should().NotContain("Editor-Version", "GitHub's login endpoints take only the user agent");
    }

    [Fact]
    public async Task Slow_down_returns_nothing_and_lengthens_the_next_wait()
    {
        var github = new CopilotAuthServer { TokenError = "slow_down", TokenInterval = 12 };
        var signIn = github.SignIn();
        var attempt = Attempt();
        await signIn.Begin(attempt, Token);
        var before = signIn.PollWait(attempt);

        var tokens = await signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        tokens.Should().BeNull();
        signIn.PollWait(attempt).Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task An_expired_device_code_refuses_the_attempt()
    {
        var github = new CopilotAuthServer { TokenError = "expired_token" };
        var signIn = github.SignIn();
        var attempt = Attempt();
        await signIn.Begin(attempt, Token);

        var finishing = () => signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
    }

    [Fact]
    public async Task Access_denied_refuses_the_attempt()
    {
        var github = new CopilotAuthServer { TokenError = "access_denied" };
        var signIn = github.SignIn();
        var attempt = Attempt();
        await signIn.Begin(attempt, Token);

        var finishing = () => signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
    }

    [Fact]
    public async Task A_verification_address_that_is_not_http_is_refused()
    {
        var github = new CopilotAuthServer { VerificationUri = "javascript:alert(1)" };
        var signIn = github.SignIn();

        var beginning = () => signIn.Begin(Attempt(), Token);

        await beginning.Should().ThrowAsync<ModelProviderException>();
    }

    [Fact]
    public async Task Exchange_sends_the_github_token_as_bearer_with_editor_headers()
    {
        var github = new CopilotAuthServer();
        var signIn = github.SignIn();
        var attempt = Attempt();
        await signIn.Begin(attempt, Token);
        github.Approve();

        var tokens = await signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        tokens.Should().NotBeNull();
        var exchange = github.Requests.Should().ContainSingle(request => request.Uri.AbsolutePath == "/copilot_internal/v2/token").Subject;
        exchange.Method.Should().Be(HttpMethod.Get);
        exchange.Headers["Authorization"].Should().Be($"Bearer {tokens!.RefreshToken}");
        exchange.Headers["Editor-Version"].Should().NotBeNullOrWhiteSpace();
        exchange.Headers["Copilot-Integration-Id"].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Changing_the_client_id_changes_what_begin_sends()
    {
        var github = new CopilotAuthServer();
        var options = new CopilotPlanOptions { ClientId = "Iv1.other-client" };
        var signIn = github.SignIn(options);

        await signIn.Begin(Attempt(), Token);

        github.Requests[0].Body.Should().Contain("client_id=Iv1.other-client");
    }

    #endregion

    #region Protected Methods

    protected override SignInRig Rig(SignInServerAnswer answer, string? endedAnswer = null)
    {
        var github = new CopilotAuthServer { RefreshAnswer = answer, EndedAnswer = endedAnswer };
        return new SignInRig(
            github.SignIn(),
            new Uri("http://127.0.0.1:1455/unused"),
            _ =>
            {
                github.Approve();
                return new ModelSignInReturn(string.Empty);
            },
            _ =>
            {
                github.TokenError = "access_denied";
                return new ModelSignInReturn(string.Empty);
            },
            () => github.TokenRequests);
    }

    #endregion

    #region Private Methods

    private static ModelSignInAttempt Attempt() => new(Guid.CreateVersion7(), "state", "verifier", "nonce", null);

    #endregion

    #region Nested Types

    /// <summary>GitHub's device, token, and Copilot exchange endpoints, played in memory.</summary>
    private sealed class CopilotAuthServer
    {
        #region Private Fields

        private int copilotTokens;
        private int refreshNumber;
        private bool approved;

        #endregion

        #region Properties

        public string VerificationUri { get; set; } = "https://github.com/login/device";

        public string? TokenError { get; set; }

        public int? TokenInterval { get; set; }

        public SignInServerAnswer RefreshAnswer { get; set; } = SignInServerAnswer.Renewed;

        public string? EndedAnswer { get; set; }

        public List<SentRequest> Requests { get; } = [];

        public List<SignInTokenRequest> TokenRequests { get; } = [];

        #endregion

        #region Public Methods

        public void Approve()
        {
            approved = true;
            TokenError = null;
        }

        public CopilotPlanSignIn SignIn(CopilotPlanOptions? options = null) =>
            new(new FixtureHttpClientFactory(new ScriptedHandler(Respond)), Microsoft.Extensions.Options.Options.Create(options ?? new CopilotPlanOptions()), TimeProvider.System, NullLogger<CopilotPlanSignIn>.Instance);

        #endregion

        #region Private Methods

        private async Task<HttpResponseMessage> Respond(HttpRequestMessage request, int _, CancellationToken cancellation)
        {
            Requests.Add(await Capture(request));
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/login/device/code")
            {
                return Json(
                    $$"""
                    {"device_code":"device-1","user_code":"ABCD-EFGH","verification_uri":"{{VerificationUri}}","expires_in":899,"interval":5}
                    """);
            }

            if (path == "/login/oauth/access_token")
            {
                TokenRequests.Add(new SignInTokenRequest("device_code", "device-1", null, null));

                if (!approved || TokenError is not null)
                {
                    var interval = TokenInterval is { } seconds ? $""","interval":{seconds}""" : string.Empty;
                    return Json($$"""{"error":"{{TokenError ?? "authorization_pending"}}"{{interval}}}""");
                }

                return Json("""{"access_token":"ghu_github-token","token_type":"bearer","scope":"read:user"}""");
            }

            if (path == "/copilot_internal/v2/token")
            {
                return Exchange();
            }

            if (path == "/user")
            {
                return Json("""{"login":"sam"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private HttpResponseMessage Exchange()
        {
            refreshNumber++;

            if (refreshNumber > 1)
            {
                return RefreshAnswer switch
                {
                    SignInServerAnswer.Ended when EndedAnswer == "403" => new HttpResponseMessage(HttpStatusCode.Forbidden),
                    SignInServerAnswer.Ended when EndedAnswer == "chat_disabled" =>
                        Json("""{"token":"tid=x;exp=1;proxy-ep=proxy.individual.githubcopilot.com","expires_at":1,"chat_enabled":false}"""),
                    SignInServerAnswer.Ended => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                    SignInServerAnswer.ServerError => new HttpResponseMessage(HttpStatusCode.BadGateway),
                    SignInServerAnswer.Unreachable => throw new HttpRequestException("unreachable"),
                    _ => Session(),
                };
            }

            return Session();
        }

        private HttpResponseMessage Session()
        {
            copilotTokens++;
            var expires = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
            return Json(
                $$"""
                {"token":"tid=session-{{copilotTokens}};exp={{expires}};proxy-ep=proxy.individual.githubcopilot.com","expires_at":{{expires}}}
                """);
        }

        private static HttpResponseMessage Json(string body) => ScriptedHandler.Json(HttpStatusCode.OK, body);

        private static async Task<SentRequest> Capture(HttpRequestMessage request)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase);
            return new SentRequest(request.Method, request.RequestUri!, headers, body);
        }

        #endregion
    }

    #endregion
}
