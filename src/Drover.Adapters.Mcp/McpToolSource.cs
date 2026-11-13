namespace Drover.Adapters.Mcp;

/// <summary>
/// Tool servers that speak the Model Context Protocol over HTTP. The protocol library does the wire work; this class
/// decides which protocol generation to ask for, how long a client lives, and what a failure means to Drover. No
/// type of the library leaves this project.
/// </summary>
/// <remarks>
/// A server of the current, stateless generation gets a client per use: nothing is held between calls. A server
/// that needs the older handshake gets one client per connection and credential, kept while it is in use and closed
/// after it has been idle, and made again when the server says its session is gone. The client offers no sampling
/// and no elicitation capability, so a server cannot ask Drover's model or its people anything.
/// </remarks>
public sealed class McpToolSource(IHttpClientFactory http, ILoggerFactory loggers, TimeProvider time) : IToolSource, IAsyncDisposable
{
    #region Private Fields

    public const string HttpClientName = "mcp";

    /// <summary>The first protocol revision with no handshake and no session.</summary>
    public const string StatelessRevision = "2026-07-28";

    /// <summary>The last revision that still uses the handshake. Asking for it forces the session-based generation.</summary>
    public const string SessionRevision = "2025-11-25";

    private static readonly TimeSpan idleLimit = TimeSpan.FromMinutes(10);

    /// <summary>The most pages of one tool list that are read. A server that keeps handing out cursors is not followed for ever.</summary>
    private const int MaxListPages = 100;

    private readonly ConcurrentDictionary<SessionKey, Session> sessions = new();

    private readonly ConcurrentDictionary<Guid, RefreshGate> gates = new();

    private readonly ILogger logger = loggers.CreateLogger<McpToolSource>();

    #endregion

    #region Properties

    public ToolSourceKind Kind => ToolSourceKind.Mcp;

    #endregion

    #region Interface Implementations

    public Task<ToolListing> ListTools(ToolEndpoint endpoint, CancellationToken cancellationToken) =>
        Use(
            endpoint,
            async (client, cancellation) =>
            {
                List<ListedTool> tools = [];
                TimeSpan? timeToLive = null;
                string? cursor = null;
                var pages = 0;

                // Each page is bounded in size by the response limit; the number of pages and of tools is bounded here.
                do
                {
                    var page = await client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, cancellation);
                    tools.AddRange(page.Tools.Take(endpoint.Limits.MaxTools - tools.Count).Select(ToListedTool));
                    timeToLive = page.TimeToLive ?? timeToLive;
                    cursor = page.NextCursor;
                }
                while (!string.IsNullOrEmpty(cursor) && ++pages < MaxListPages && tools.Count < endpoint.Limits.MaxTools);

                return new ToolListing(tools, client.NegotiatedProtocolVersion ?? string.Empty, IsStateless(client), timeToLive);
            },
            mayRepeat: true,
            cancellationToken);

    public Task<ToolOutcome> CallTool(ToolEndpoint endpoint, string toolName, JsonElement arguments, CancellationToken cancellationToken) =>
        Use(
            endpoint,
            async (client, cancellation) =>
            {
                var result = await client.CallToolAsync(
                    new CallToolRequestParams { Name = toolName, Arguments = ToArguments(arguments) },
                    cancellation);
                return new ToolOutcome(ToJson(result), result.IsError is true);
            },
            mayRepeat: false,
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        foreach (var key in sessions.Keys)
        {
            await Close(key);
        }

        foreach (var gate in gates.Values)
        {
            gate.Dispose();
        }
    }

    #endregion

    #region Client Lifetime

    /// <summary>
    /// Runs the work on a client for the endpoint, within the endpoint's timeout. A kept session whose server says
    /// it has forgotten it is always closed. Work that only reads (<paramref name="mayRepeat"/>) is then done once
    /// more on a new session. A tool call never is: the server may have acted before it answered, and a server can
    /// say "session gone" whenever it likes, so the failure is reported and the next call opens a session of its own.
    /// </summary>
    private async Task<T> Use<T>(
        ToolEndpoint endpoint,
        Func<McpClient, CancellationToken, Task<T>> work,
        bool mayRepeat,
        CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(endpoint.Timeout);

        // Set before any client is made, so every request of this use, and of a session opened by it, is bounded.
        var bound = ResponseLimit.Begin(endpoint.Limits.MaxResponseBytes);
        var signIn = SignInAttempt.Begin(endpoint);
        await CloseIdleSessions();
        var key = new SessionKey(endpoint.ConnectionId, endpoint.CredentialKey);

        // A use that may ask a person is a sign-in in progress, for a credential that may not exist yet. It gets a
        // client of its own and leaves none behind, so no session is ever shared between two people signing in.
        var keeps = endpoint.SignIn?.AskPerson is null;

        try
        {
            if (keeps && sessions.TryGetValue(key, out var kept))
            {
                try
                {
                    return await kept.Use(work, time, limit.Token);
                }
                catch (Exception gone) when (IsSessionGone(gone))
                {
                    // The library's failure may carry the server's own text, so only its type is logged.
                    logger.LogInformation(
                        "The session with tool connection {ConnectionId} is gone ({Failure}); repeating the request: {Repeating}",
                        endpoint.ConnectionId,
                        gone.GetType().Name,
                        mayRepeat);
                    await Close(key);

                    if (!mayRepeat)
                    {
                        throw;
                    }
                }
            }

            var client = await Connect(endpoint, limit.Token);

            if (!keeps || IsStateless(client))
            {
                await using (client)
                {
                    return await work(client, limit.Token);
                }
            }

            var session = new Session(client, time.GetTimestamp());

            if (!sessions.TryAdd(key, session))
            {
                // Another call opened one at the same moment. Use this one for this call and let it go.
                await using (client)
                {
                    return await work(client, limit.Token);
                }
            }

            return await session.Use(work, time, limit.Token);
        }
        catch (Exception failure) when (signIn.NamedAnotherService && !cancellationToken.IsCancellationRequested)
        {
            throw new ToolSourceException(
                ToolSourceFailure.Blocked,
                "The tool server names a different sign-in service than the one this connection was set up with. Nothing was sent to it.",
                failure);
        }
        catch (Exception failure) when (signIn.RenewalWasNotAnswered && !cancellationToken.IsCancellationRequested)
        {
            // The library reports "renewal failed" and "renewal refused" alike, by asking for a new sign-in. Only a
            // refusal ends a sign-in. A service that is down or overloaded leaves it as good as it was.
            await Close(key);
            throw new ToolSourceException(
                signIn.RenewalStatus is 0 ? ToolSourceFailure.Unreachable : ToolSourceFailure.ServerError,
                "The tool server's sign-in service could not renew the sign-in just now. The sign-in itself is unchanged; try again later.",
                failure);
        }
        catch (Exception failure) when (signIn.WasRequired && signIn.LastRefusal is HttpStatusCode.Forbidden && !cancellationToken.IsCancellationRequested)
        {
            // The account is signed in and may not do this one thing. That is not a sign-in that ended.
            throw new ToolSourceException(
                ToolSourceFailure.Unauthorized,
                "The tool server does not let the signed-in account do this.",
                failure);
        }
        catch (Exception failure) when (signIn.WasRequired && !cancellationToken.IsCancellationRequested)
        {
            // The session, if one was kept, was opened with tokens that no longer work.
            await Close(key);
            throw new ToolSourceException(ToolSourceFailure.SignInRequired, "This connection needs a sign-in before it can be used.", failure);
        }
        catch (Exception failure) when (failure is not ToolSourceException && !cancellationToken.IsCancellationRequested)
        {
            // A kept session whose stream was cut for size is of no further use.
            if (bound.WasReached)
            {
                await Close(key);
                throw new ToolSourceException(ToolSourceFailure.TooLarge, "The tool server's answer was larger than this server accepts.", failure);
            }

            throw Failures.Translate(failure, limit.IsCancellationRequested);
        }
        finally
        {
            signIn.Release();
        }
    }

    private async Task<McpClient> Connect(ToolEndpoint endpoint, CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint.Url,
                TransportMode = endpoint.Transport is ToolTransport.Sse ? HttpTransportMode.Sse : HttpTransportMode.AutoDetect,
                AdditionalHeaders = new Dictionary<string, string>(endpoint.Headers),
                ConnectionTimeout = endpoint.Timeout,
                OAuth = endpoint.SignIn is { } signIn ? ToOptions(signIn, endpoint) : null,
            },
            http.CreateClient(HttpClientName),
            loggers,
            ownsHttpClient: true);
        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "Drover", Version = "0" },
            ProtocolVersion = endpoint.ProtocolMode switch
            {
                ProtocolMode.StatelessOnly => StatelessRevision,
                ProtocolMode.SessionOnly => SessionRevision,
                _ => null,
            },
        };

        try
        {
            return await McpClient.CreateAsync(transport, options, loggers, cancellationToken);
        }
        catch (Exception failure) when (endpoint.ProtocolMode is ProtocolMode.StatelessOnly && Failures.IsRefusalOfTheProtocol(failure))
        {
            throw new ToolSourceException(
                ToolSourceFailure.ProtocolNotSupported,
                "This server does not speak the stateless protocol, and the connection is set to stateless only. Set it to automatic to use the server's older protocol.",
                failure);
        }
    }

    /// <summary>
    /// The library does the sign-in: discovery, the proof key, the exchange, renewal. This says who this server is,
    /// in the order the protocol asks for (a client id a person entered, else the client document, else registering
    /// itself), where the tokens are kept, and how a person is asked. The person is found through the use in
    /// progress and not through the endpoint that opened a session, so a kept session never asks anyone.
    /// </summary>
    private ClientOAuthOptions ToOptions(ToolSignIn signIn, ToolEndpoint endpoint)
    {
        var options = new ClientOAuthOptions
        {
            RedirectUri = signIn.RedirectUrl,
            ClientId = signIn.ClientId,
            ClientSecret = signIn.ClientSecret,
            ClientMetadataDocumentUri = signIn.ClientId is null ? signIn.ClientDocumentUrl : null,
            DynamicClientRegistration = new DynamicClientRegistrationOptions { ClientName = signIn.ClientName },
            TokenCache = new KeptTokenCache(signIn, gates.GetOrAdd(endpoint.CredentialKey ?? endpoint.ConnectionId, _ => new RefreshGate()), time),
            AuthorizationCallbackHandler = async (context, cancellationToken) =>
            {
                var attempt = SignInAttempt.Current;

                if (attempt?.Endpoint.SignIn?.AskPerson is not { } ask)
                {
                    throw (attempt ?? SignInAttempt.Begin(endpoint)).Require();
                }

                var answer = await ask(new SignInPrompt(context.AuthorizationUri), cancellationToken);
                return new AuthorizationResult { Code = answer.Code, State = answer.State, Iss = answer.Issuer };
            },
        };

        if (signIn.Issuer is { } issuer)
        {
            // A connection pinned to one sign-in service uses that one or none. The tool server's documents say
            // which services it accepts, and they are its own to change: without the pin, a client secret, a code
            // and its proof key, and a refresh token would follow them wherever they point.
            options.AuthServerSelector = offered =>
            {
                var pinned = offered.FirstOrDefault(candidate => IsSameService(candidate, issuer));

                if (pinned is null && SignInAttempt.Current is { } attempt)
                {
                    attempt.NamedAnotherService = true;
                }

                return pinned;
            };
        }

        return options;
    }

    private static bool IsSameService(Uri one, Uri other) =>
        one.Scheme == other.Scheme
        && one.Port == other.Port
        && string.Equals(one.IdnHost, other.IdnHost, StringComparison.OrdinalIgnoreCase)
        && string.Equals(one.AbsolutePath.TrimEnd('/'), other.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);

    private static bool IsStateless(McpClient client) =>
        string.CompareOrdinal(client.NegotiatedProtocolVersion, StatelessRevision) >= 0 && client.SessionId is null;

    private static bool IsSessionGone(Exception failure) =>
        failure is ClientTransportClosedException or ObjectDisposedException
        || failure is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
        || (failure.InnerException is { } inner && IsSessionGone(inner));

    private async Task CloseIdleSessions()
    {
        foreach (var (key, session) in sessions)
        {
            if (session.IsIdle(time, idleLimit))
            {
                await Close(key);
            }
        }
    }

    private async Task Close(SessionKey key)
    {
        if (!sessions.TryRemove(key, out var session))
        {
            return;
        }

        try
        {
            await session.Client.DisposeAsync();
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogDebug(failure, "Closing the session with tool connection {ConnectionId} failed", key.ConnectionId);
        }
    }

    #endregion

    #region Translation

    private static ListedTool ToListedTool(ServerTool tool) =>
        new(tool.Name, tool.Title ?? tool.Annotations?.Title, tool.Description ?? string.Empty, tool.InputSchema.Clone())
        {
            Annotations = tool.Annotations is null ? null : JsonSerializer.SerializeToElement(tool.Annotations, McpJsonUtilities.DefaultOptions),
            ClaimsReadOnly = tool.Annotations?.ReadOnlyHint is true,
            ClaimsDestructive = tool.Annotations?.DestructiveHint is true,
        };

    private static Dictionary<string, JsonElement> ToArguments(JsonElement arguments) =>
        arguments.ValueKind is JsonValueKind.Object
            ? arguments.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
            : [];

    /// <summary>
    /// A result as JSON the engine can store: the structured content when the server sent one, otherwise the text
    /// blocks joined. A block that is not text is named, not carried: files and images come with the file store.
    /// </summary>
    private static JsonElement ToJson(CallToolResult result)
    {
        if (result.StructuredContent is { } structured)
        {
            return JsonSerializer.SerializeToElement(structured, McpJsonUtilities.DefaultOptions);
        }

        var text = string.Join(
            "\n",
            result.Content.Select(block => block is TextContentBlock words ? words.Text : $"[{block.Type} content is not shown]"));
        return JsonSerializer.SerializeToElement(text);
    }

    #endregion

    #region Nested Types

    private sealed record SessionKey(Guid ConnectionId, Guid? CredentialKey);

    private sealed class Session(McpClient client, long openedAt)
    {
        #region Private Fields

        private long lastUsedAt = openedAt;

        private int inUse;

        #endregion

        #region Properties

        public McpClient Client => client;

        #endregion

        #region Public Methods

        public bool IsIdle(TimeProvider time, TimeSpan limit) =>
            Volatile.Read(ref inUse) == 0 && time.GetElapsedTime(Volatile.Read(ref lastUsedAt)) >= limit;

        public async Task<T> Use<T>(Func<McpClient, CancellationToken, Task<T>> work, TimeProvider time, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref inUse);

            try
            {
                return await work(client, cancellationToken);
            }
            finally
            {
                Volatile.Write(ref lastUsedAt, time.GetTimestamp());
                Interlocked.Decrement(ref inUse);
            }
        }

        #endregion
    }

    #endregion
}
