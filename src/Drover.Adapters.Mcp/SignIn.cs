namespace Drover.Adapters.Mcp;

/// <summary>
/// One use of a tool server, as the sign-in code of the protocol library sees it. The library calls back from
/// inside the request, so what the use allows travels with the flow of the call and not through the library:
/// whether a person can be asked, and whether a sign-in turned out to be needed and impossible.
/// </summary>
public sealed class SignInAttempt
{
    #region Private Fields

    private static readonly AsyncLocal<SignInAttempt?> current = new();

    private RefreshGate? held;

    #endregion

    #region Constructors

    private SignInAttempt(ToolEndpoint endpoint) => Endpoint = endpoint;

    #endregion

    #region Properties

    public static SignInAttempt? Current => current.Value;

    public ToolEndpoint Endpoint { get; }

    /// <summary>Set when the server wanted a sign-in and nobody was there to give one.</summary>
    public bool WasRequired { get; private set; }

    #endregion

    #region Public Methods

    public static SignInAttempt Begin(ToolEndpoint endpoint)
    {
        var attempt = new SignInAttempt(endpoint);
        current.Value = attempt;
        return attempt;
    }

    public SignInRequiredException Require()
    {
        WasRequired = true;
        return new SignInRequiredException();
    }

    /// <summary>Remembers that this use is the one renewing a credential's tokens, until it has stored new ones or ended.</summary>
    public void Hold(RefreshGate gate) => held = gate;

    public bool Holds(RefreshGate gate) => ReferenceEquals(held, gate);

    public void Release()
    {
        Interlocked.Exchange(ref held, null)?.Leave();
    }

    #endregion
}

/// <summary>Thrown into the library when it asks for a person and there is none. It carries nothing.</summary>
public sealed class SignInRequiredException() : Exception("A sign-in is required.");

/// <summary>
/// Lets one use at a time renew a credential's tokens. A refresh token often works once: two uses that both find
/// the access token run out and both spend the refresh token would leave one of them, and the stored credential,
/// with nothing that works.
/// </summary>
public sealed class RefreshGate : IDisposable
{
    #region Private Fields

    private readonly SemaphoreSlim turn = new(1, 1);

    #endregion

    #region Public Methods

    public Task Enter(CancellationToken cancellationToken) => turn.WaitAsync(cancellationToken);

    public void Leave() => turn.Release();

    #endregion

    #region Interface Implementations

    public void Dispose() => turn.Dispose();

    #endregion
}

/// <summary>
/// One credential's tokens, kept by the caller. The library reads them before every request and stores them after
/// every sign-in and renewal. What is stored is the library's own token set as JSON; the caller treats it as a secret
/// it cannot read.
/// </summary>
internal sealed class KeptTokenCache(ToolSignIn signIn, RefreshGate gate, TimeProvider time) : ITokenCache
{
    #region Private Fields

    /// <summary>A token this close to running out is treated as run out, so it is renewed by one use and not raced for.</summary>
    private static readonly TimeSpan margin = TimeSpan.FromSeconds(30);

    #endregion

    #region Interface Implementations

    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        var tokens = await Read(cancellationToken);

        // Outside a use (the listening stream of a kept session) nothing is renewed, so there is nothing to wait for.
        if (tokens is null || !IsRunningOut(tokens) || SignInAttempt.Current is not { } attempt || attempt.Holds(gate))
        {
            return tokens;
        }

        await gate.Enter(cancellationToken);
        tokens = await Read(cancellationToken);

        if (tokens is null || !IsRunningOut(tokens))
        {
            // Another use renewed them while this one waited.
            gate.Leave();
            return tokens;
        }

        attempt.Hold(gate);
        return tokens;
    }

    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        await signIn.WriteTokens(JsonSerializer.Serialize(tokens, McpJsonUtilities.DefaultOptions), cancellationToken);

        if (SignInAttempt.Current is { } attempt && attempt.Holds(gate))
        {
            attempt.Release();
        }
    }

    #endregion

    #region Private Methods

    private async Task<TokenContainer?> Read(CancellationToken cancellationToken) =>
        await signIn.ReadTokens(cancellationToken) is { Length: > 0 } stored
            ? JsonSerializer.Deserialize<TokenContainer>(stored, McpJsonUtilities.DefaultOptions)
            : null;

    private bool IsRunningOut(TokenContainer tokens) =>
        tokens.ExpiresIn is { } seconds && tokens.ObtainedAt.AddSeconds(seconds) - margin <= time.GetUtcNow();

    #endregion
}

/// <summary>
/// Keeps credentials where they belong, whatever a tool server's documents say. A request that carries an access
/// token goes to the tool server's own origin or is not sent; a connection saved as <c>https</c> makes no request
/// over <c>http</c>, so no code, token, or client secret travels in the clear because a document named an
/// <c>http</c> address. A request made outside any use of a connection carries no access token at all.
/// </summary>
public sealed class CredentialGuardHandler : DelegatingHandler
{
    #region Overrides

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var endpoint = SignInAttempt.Current?.Endpoint.Url;
        var target = request.RequestUri;
        var carriesToken = request.Headers.Authorization is { Scheme: var scheme } && scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase);

        if (target is null
            || (carriesToken && (endpoint is null || !SameOrigin(endpoint, target)))
            || (endpoint is { Scheme: "https" } && target.Scheme is not "https"))
        {
            throw new ToolSourceException(
                ToolSourceFailure.Blocked,
                "The tool server pointed this server's credentials at an address they must not go to. Nothing was sent.");
        }

        return base.SendAsync(request, cancellationToken);
    }

    #endregion

    #region Private Methods

    private static bool SameOrigin(Uri one, Uri other) =>
        one.Scheme == other.Scheme && one.Port == other.Port && string.Equals(one.IdnHost, other.IdnHost, StringComparison.OrdinalIgnoreCase);

    #endregion
}
