namespace Drover.Adapters.Conformance.Tools;

/// <summary>Which protocol generations a test tool server speaks.</summary>
public enum ServerGeneration
{
    /// <summary>The current generation only: no handshake, no session.</summary>
    Stateless,

    /// <summary>The older generation only: a handshake and a session. A request of the current generation is refused.</summary>
    SessionBased,

    /// <summary>Both on one address.</summary>
    Both,

    /// <summary>The older generation over the oldest transport, with its separate event stream.</summary>
    LegacySse,
}

/// <summary>How to stand a test tool server up. With a header named, a request without that exact header gets 401.</summary>
public sealed record ToolServerSpec(ServerGeneration Generation, string? HeaderName = null, string? HeaderValue = null)
{
    /// <summary>When set, the server answers every request with a redirect to this address and does nothing else.</summary>
    public Uri? RedirectTo { get; init; }

    /// <summary>When set, the server wants a sign-in: a request without an access token it issued gets 401 and a pointer to its sign-in.</summary>
    public ToolServerSignIn? SignIn { get; init; }
}

/// <summary>
/// How a test tool server signs people in. It always requires the proof key and returns the issuer with the code.
/// A refresh token works once: using it gives a new one, and using an old one again is refused.
/// </summary>
public sealed record ToolServerSignIn
{
    #region Properties

    /// <summary>Whether a client it has never seen may register itself.</summary>
    public bool RegistersClients { get; init; } = true;

    /// <summary>Whether it takes the address of a client document as a client id.</summary>
    public bool ReadsClientDocuments { get; init; }

    /// <summary>A client id the server already knows, as if an admin had registered one by hand.</summary>
    public string? KnownClientId { get; init; }

    /// <summary>How long an access token is good for.</summary>
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromHours(1);

    #endregion
}

/// <summary>
/// A tool server a conformance run talks to over real HTTP. It offers four tools: <c>echo</c> (says it only reads;
/// returns "echo: " and its <c>text</c> argument), <c>record</c> (says it is destructive; returns the structured
/// content <c>{"recorded":true}</c>), <c>fail</c> (runs and reports an error), <c>slow</c> (never answers), and
/// <c>flood</c> (answers with 300,000 characters).
/// A call to a tool named <c>vanish</c> is taken, counted, and answered as if the server had lost the caller's
/// session: what a server that acted and then died, or a hostile one, looks like from outside.
/// </summary>
public interface IToolServer : IAsyncDisposable
{
    Uri Url { get; }

    /// <summary>How many handshakes the server has answered: one per session opened.</summary>
    int Handshakes { get; }

    /// <summary>How many calls to the <c>vanish</c> tool reached the server.</summary>
    int VanishedCalls { get; }

    /// <summary>How many times a person approved a sign-in.</summary>
    int Approvals { get; }

    /// <summary>How many times a refresh token was exchanged for new tokens.</summary>
    int Refreshes { get; }

    /// <summary>How many clients registered themselves.</summary>
    int Registrations { get; }

    /// <summary>The client ids the server has seen at its approval page, oldest first.</summary>
    IReadOnlyList<string> ClientsSeen { get; }

    /// <summary>
    /// Plays the person: opens the approval address as a browser would, approves, and returns what the browser
    /// would carry back to the redirect address.
    /// </summary>
    Task<SignInAnswer> Approve(Uri authorizationUrl);

    /// <summary>From now on the server wants this value in its header, and refuses the one it wanted before.</summary>
    void ExpectHeader(string value);

    /// <summary>How many requests of any kind reached the server's token endpoint.</summary>
    int TokenRequests { get; }

    /// <summary>
    /// Makes the token endpoint answer every renewal with this status and change nothing, as a sign-in service that
    /// is overloaded or down does. Null makes it work again.
    /// </summary>
    void FailRenewals(int? status);

    /// <summary>
    /// Makes the server answer every tool call with 403 and a note that the access token lacks a scope, as a server
    /// does when the signed-in account may not use that tool. Listing still works.
    /// </summary>
    void ForbidCalls(bool forbidden);

    /// <summary>Forgets every access token, as if all had expired. Refresh tokens stay good.</summary>
    void ExpireAccessTokens();

    /// <summary>Forgets every token of both kinds, as if the person had withdrawn their consent.</summary>
    void WithdrawConsent();

    /// <summary>Stops the server and starts it again on the same address, with every session forgotten.</summary>
    Task Restart();

    /// <summary>Stops the server. Its address then refuses connections.</summary>
    Task ShutDown();
}
