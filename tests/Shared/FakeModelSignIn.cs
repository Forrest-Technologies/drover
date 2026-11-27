namespace Drover.Tests.Common;

/// <summary>How the fake provider answers the next refresh.</summary>
public enum FakeRefreshAnswer
{
    Renewed,
    Ended,
    ServerError,
    Unreachable,
}


/// <summary>One request that reached the fake provider's token address.</summary>
public sealed record FakeTokenRequest(string Grant, string? Code, string? Verifier, string? RefreshToken);


/// <summary>
/// A model provider's sign-in, played in memory: an authorization server that takes a PKCE challenge, hands out a
/// one-time code when the person approves, exchanges it for a token set, and rotates refresh tokens, refusing one
/// that was already spent. It stands for the provider, outside any Drover process, and records every request made
/// to its token address. Every token it makes is random, so a test can look for it where it must never appear.
/// </summary>
public sealed class FakeModelSignIn(
    string kind = FakeModelSignIn.PlanKind,
    bool isPersonalPlan = true,
    TimeProvider? time = null) : IModelSignIn
{
    #region Private Fields

    /// <summary>A kind that signs in with a person's own paid plan.</summary>
    public const string PlanKind = "test_plan_signin";

    /// <summary>A kind whose flow the provider offers to any app.</summary>
    public const string OpenKind = "test_open_signin";

    public const string AccountLabel = "sam@example.test";

    private readonly ConcurrentDictionary<string, string> challengesByState = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string> challengesByCode = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, bool> spent = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<FakeTokenRequest> received = new();

    private readonly ConcurrentQueue<string> issued = new();

    private readonly ConcurrentQueue<string> revoked = new();

    private readonly TimeProvider clock = time ?? TimeProvider.System;

    private readonly ConcurrentQueue<IReadOnlyDictionary<string, string>> earliers = new();

    #endregion

    #region Properties

    public static Uri AuthorizeAddress { get; } = new("https://auth.example.test/authorize");

    public static Uri RedirectUri { get; } = new("http://127.0.0.1:1455/auth/callback");

    public string Kind => kind;

    public string DisplayName => kind == PlanKind ? "Test plan" : "Test account";

    public bool IsPersonalPlan => isPersonalPlan;

    /// <summary>How the next refreshes are answered.</summary>
    public FakeRefreshAnswer RefreshAnswer { get; set; } = FakeRefreshAnswer.Renewed;

    /// <summary>How long an access token lasts.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>When set, a refresh waits for it before it answers, so a test can hold two callers at the same moment.</summary>
    public TaskCompletionSource? RefreshGate { get; set; }

    /// <summary>Called each time the provider has handed out a new token set, before the caller has it: a test can cancel the call there.</summary>
    public Action? AfterTokens { get; set; }

    public IReadOnlyList<FakeTokenRequest> Received => [.. received];

    public int Refreshes => received.Count(request => request.Grant == "refresh_token");

    public int Begins { get; private set; }

    /// <summary>What each attempt was handed of the person's earlier sign-in, in the order the attempts began.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Earliers => [.. earliers];

    /// <summary>When set, an approval is refused with this sentence, as a provider that signed the person in but allowed too little.</summary>
    public string? RefusalSentence { get; set; }

    /// <summary>Every access and refresh token this provider has handed out.</summary>
    public IReadOnlyList<string> Issued => [.. issued];

    /// <summary>The access tokens of the sign-ins the provider was told are over.</summary>
    public IReadOnlyList<string> Revoked => [.. revoked];

    #endregion

    #region Public Methods

    /// <summary>What the person's device receives once they approve on the provider's page.</summary>
    public ModelSignInReturn Approve(string state)
    {
        var code = $"code-{Guid.NewGuid():N}";
        challengesByCode[code] = challengesByState.GetValueOrDefault(state) ?? string.Empty;
        return new ModelSignInReturn($"code={code}&state={Uri.EscapeDataString(state)}");
    }

    /// <summary>What the person's device receives when they refuse.</summary>
    public static ModelSignInReturn Refuse(string state) => new($"error=access_denied&state={Uri.EscapeDataString(state)}");

    /// <summary>The state an address from <see cref="Begin"/> carries.</summary>
    public static string StateOf(Uri address) => Read(address.Query)["state"];

    #endregion

    #region Interface Implementations

    public bool AcceptsRedirect(Uri? redirectUri) =>
        redirectUri is not null
        && redirectUri.Scheme == RedirectUri.Scheme
        && redirectUri.Host == RedirectUri.Host
        && redirectUri.AbsolutePath == RedirectUri.AbsolutePath
        && redirectUri.Query.Length == 0;

    public Task<ModelSignInStep> Begin(ModelSignInAttempt attempt, CancellationToken cancellation)
    {
        Begins++;
        earliers.Enqueue(attempt.Earlier);
        var challenge = System.Buffers.Text.Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.Verifier)));
        challengesByState[attempt.State] = challenge;
        var address = new UriBuilder(AuthorizeAddress)
        {
            Query = $"response_type=code&code_challenge={challenge}&code_challenge_method=S256&state={Uri.EscapeDataString(attempt.State)}&redirect_uri={Uri.EscapeDataString(attempt.RedirectUri?.AbsoluteUri ?? string.Empty)}",
        }.Uri;
        return Task.FromResult(new ModelSignInStep(address, null, ModelSignInWait.Return));
    }

    public Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation)
    {
        var answer = Read(arrived.Text);

        if (answer.ContainsKey("error") || answer.GetValueOrDefault("state") != attempt.State || answer.GetValueOrDefault("code") is not { } code)
        {
            throw new ModelSignInRefusedException();
        }

        if (RefusalSentence is { } sentence)
        {
            throw new ModelSignInRefusedException(sentence);
        }

        received.Enqueue(new FakeTokenRequest("authorization_code", code, attempt.Verifier, null));
        var expected = System.Buffers.Text.Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.Verifier)));

        if (!challengesByCode.TryRemove(code, out var challenge) || challenge != expected)
        {
            throw new ModelSignInRefusedException();
        }

        // What a provider keeps for a later sign-in, such as a client id it issued to the person.
        return Task.FromResult<ModelTokens?>(NewTokens() with { Details = new Dictionary<string, string> { ["client"] = $"fake-client-{Guid.NewGuid():N}" } });
    }

    public async Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation)
    {
        received.Enqueue(new FakeTokenRequest("refresh_token", null, null, current.RefreshToken));

        if (RefreshGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellation);
        }

        return RefreshAnswer switch
        {
            FakeRefreshAnswer.Ended => throw new SignInEndedException(),
            FakeRefreshAnswer.ServerError => throw ModelProviderException.FromStatus(503),
            FakeRefreshAnswer.Unreachable => throw ModelProviderException.FromNetwork(),
            _ when current.RefreshToken is null || !spent.TryAdd(current.RefreshToken, true) => throw new SignInEndedException(),
            _ => NewTokens() with { Details = current.Details },
        };
    }

    public Task Revoke(ModelTokens current, CancellationToken cancellation)
    {
        revoked.Enqueue(current.AccessToken);
        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    private ModelTokens NewTokens()
    {
        var tokens = new ModelTokens
        {
            AccessToken = $"fake-access-{Guid.NewGuid():N}",
            RefreshToken = $"fake-refresh-{Guid.NewGuid():N}",
            ExpiresAt = clock.GetUtcNow() + Lifetime,
            Scopes = ["models"],
            AccountLabel = AccountLabel,
        };
        issued.Enqueue(tokens.AccessToken);
        issued.Enqueue(tokens.RefreshToken);
        AfterTokens?.Invoke();
        return tokens;
    }

    private static Dictionary<string, string> Read(string query) =>
        query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty, StringComparer.Ordinal);

    #endregion
}
