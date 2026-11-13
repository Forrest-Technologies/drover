namespace Drover.Engine.Tools;

/// <summary>How starting a sign-in went.</summary>
public enum SignInStep
{
    /// <summary>The person must open <see cref="SignInStart.AuthorizationUrl"/> in their browser.</summary>
    Redirect,

    /// <summary>Tokens that still work were found. Nothing more to do.</summary>
    Connected,

    Failed,

    NotFound,
}

/// <summary>What a person gets back when they ask to sign in to a tool server.</summary>
public sealed record SignInStart(SignInStep Step, Uri? AuthorizationUrl = null, string? Problem = null);

/// <summary>How a sign-in ended when the browser came back. The ids are set once the sign-in was found to be the caller's own.</summary>
public sealed record SignInFinish(bool Connected, Guid? SpaceId, Guid? ConnectionId, string? Problem);

/// <summary>
/// Signs people in to tool servers. The protocol library runs a sign-in as one call that waits for the code, and
/// the code arrives in a different request, from the person's browser. So a sign-in is a background task that
/// parks where the library asks for the code, and the callback hands the code to it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A sign-in is found again by the hash of its <c>state</c>, stored in <c>oauth_flows</c> with the person and
/// connection. The callback completes it only for the signed-in person who started it, once, within ten minutes.</item>
/// <item>The waiting half lives in this process. If the server restarts, or the callback reaches another instance,
/// the sign-in is gone and the person starts again; nothing is left half-done, because tokens are stored only after
/// the exchange.</item>
/// <item>One person has at most one sign-in in progress per connection. Starting another ends the first.</item>
/// <item>No transaction is open while anything waits: each read and write is a short transaction of its own.</item>
/// </list>
/// </remarks>
public sealed class ToolSignIns(
    IServiceScopeFactory scopes,
    IEnumerable<IToolSource> sources,
    TimeProvider time,
    ILogger<ToolSignIns> logger) : IAsyncDisposable
{
    #region Private Fields

    /// <summary>How long a person has to come back from the tool server's sign-in page.</summary>
    public static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long the callback waits for the exchange and the first listing before it answers.</summary>
    private static readonly TimeSpan finishPatience = TimeSpan.FromSeconds(45);

    private readonly ConcurrentDictionary<string, Pending> byState = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<(Guid UserId, Guid ConnectionId), Pending> byPerson = new();

    #endregion

    #region Public Methods

    /// <summary>
    /// Starts a sign-in for the person, to their own credential or, where one sign-in serves the space, to the
    /// space's. Returns when the tool server has said where the person must go, or when it turned out that no
    /// sign-in is needed, or that none is possible.
    /// </summary>
    public async Task<SignInStart> Begin(Guid spaceId, Guid userId, Guid connectionId, CancellationToken cancellationToken)
    {
        if (sources.FirstOrDefault(found => found.Kind is ToolSourceKind.Mcp) is not { } source)
        {
            return new SignInStart(SignInStep.Failed, Problem: "This server has no adapter for tool connections.");
        }

        var tenant = new RunTenant(scopes, spaceId, userId);
        var prepared = await tenant.Transact(services => Prepare(services, connectionId, userId, cancellationToken), cancellationToken);

        if (prepared.Endpoint is not { } endpoint || prepared.Policy is not { } policy)
        {
            return new SignInStart(prepared.Problem is null ? SignInStep.NotFound : SignInStep.Failed, Problem: prepared.Problem);
        }

        var pending = new Pending(tenant, connectionId);

        if (byPerson.TryRemove((userId, connectionId), out var earlier))
        {
            await earlier.Cancel.CancelAsync();
        }

        byPerson[(userId, connectionId)] = pending;

        // The task outlives this request on purpose: it waits for the person's browser to come back.
        pending.Finished = Task.Run(() => Run(source, endpoint, policy, pending), CancellationToken.None);
        await Task.WhenAny(pending.Asked.Task, pending.Finished).WaitAsync(cancellationToken);

        if (pending.Asked.Task.IsCompletedSuccessfully)
        {
            return new SignInStart(SignInStep.Redirect, await pending.Asked.Task);
        }

        var problem = await pending.Finished;
        return problem is null ? new SignInStart(SignInStep.Connected) : new SignInStart(SignInStep.Failed, Problem: problem);
    }

    /// <summary>
    /// Takes what the person's browser brought back from the tool server's sign-in page. The <c>state</c> must name
    /// a sign-in that this same person started and that is still waiting; anything else changes nothing.
    /// </summary>
    public async Task<SignInFinish> Complete(Guid userId, string? state, string? code, string? issuer, bool refused, CancellationToken cancellationToken)
    {
        const string NotWaiting = "This sign-in is not waiting any more. Start it again from the connection.";

        if (string.IsNullOrEmpty(state))
        {
            return new SignInFinish(false, null, null, NotWaiting);
        }

        var hash = Hash(state);
        var flow = await Resolve(userId, hash, cancellationToken);

        // The row says whose sign-in this is. A person who is handed someone else's callback address completes nothing.
        if (flow is null || flow.UserId != userId)
        {
            return new SignInFinish(false, null, null, NotWaiting);
        }

        if (!byState.TryRemove(hash, out var pending))
        {
            // The waiting half was lost with a restart, or lives in another instance.
            await new RunTenant(scopes, flow.SpaceId, userId).Transact(
                services => services.GetRequiredService<ToolConnectionRepository>().RemoveFlow(hash, cancellationToken),
                cancellationToken);
            return new SignInFinish(false, flow.SpaceId, flow.ToolConnectionId, NotWaiting);
        }

        if (refused || string.IsNullOrEmpty(code))
        {
            pending.Refused = true;
            pending.Answered.TrySetException(new OperationCanceledException("The sign-in was not approved."));
        }
        else
        {
            pending.Answered.TrySetResult(new SignInAnswer(code, state, string.IsNullOrEmpty(issuer) ? null : issuer));
        }

        try
        {
            var problem = await pending.Finished.WaitAsync(finishPatience, time, cancellationToken);
            return new SignInFinish(problem is null, flow.SpaceId, flow.ToolConnectionId, problem);
        }
        catch (TimeoutException slow)
        {
            logger.LogInformation("The sign-in to tool connection {ConnectionId} had not finished when its callback answered: {Failure}", flow.ToolConnectionId, slow.GetType().Name);
            return new SignInFinish(false, flow.SpaceId, flow.ToolConnectionId, "The tool server is taking a long time to finish the sign-in. Check the connection in a moment.");
        }
    }

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        foreach (var pending in byPerson.Values)
        {
            await pending.Cancel.CancelAsync();
        }
    }

    #endregion

    #region Private Methods

    /// <summary>Reads, as the person, what the sign-in needs. A connection that is not theirs to sign in to yields a problem or nothing.</summary>
    private static async Task<(ToolEndpoint? Endpoint, EffectivePolicy? Policy, string? Problem)> Prepare(
        IServiceProvider services,
        Guid connectionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connection = await services.GetRequiredService<ToolConnectionRepository>().FindConnection(connectionId, cancellationToken);

        if (connection is null)
        {
            return (null, null, null);
        }

        if (connection.AuthKind is not ToolAuthKind.Oauth)
        {
            return (null, null, "This connection does not use a sign-in.");
        }

        if (!Uri.TryCreate(connection.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            return (null, null, "The connection's address is not a web address.");
        }

        var policies = services.GetRequiredService<PolicyService>();
        var signIn = await services.GetRequiredService<ToolConnectionService>().SignInFor(connection, userId, cancellationToken);

        if (signIn.SignIn is null)
        {
            return (null, null, signIn.Problem);
        }

        var endpoint = new ToolEndpoint(connection.Id, url)
        {
            Transport = connection.Transport,
            ProtocolMode = connection.ProtocolMode,
            Limits = new ToolServerLimits(policies.Deployment.MaxToolResponseBytes, policies.Deployment.MaxToolsPerConnection),
            Timeout = FlowLifetime,
            SignIn = signIn.SignIn,
        };
        return (endpoint, await policies.GetEffective(connection.SpaceId, cancellationToken), null);
    }

    /// <summary>The whole sign-in: connect, be sent to ask the person, wait, exchange, list, store. Returns null when it worked, else why not.</summary>
    private async Task<string?> Run(IToolSource source, ToolEndpoint endpoint, EffectivePolicy policy, Pending pending)
    {
        try
        {
            using var attempt = OutboundAttempt.Begin(policy);
            var asking = endpoint with { SignIn = endpoint.SignIn! with { AskPerson = (prompt, cancellation) => Ask(endpoint, pending, prompt, cancellation) } };
            var listing = await source.ListTools(asking, pending.Cancel.Token);
            await pending.Tenant.InScope(
                services => services.GetRequiredService<ToolConnectionService>().Record(endpoint.ConnectionId, listing, null, null, CancellationToken.None));
            logger.LogInformation("A person signed in to tool connection {ConnectionId}", endpoint.ConnectionId);
            return null;
        }
        catch (ToolSourceException failure)
        {
            logger.LogInformation("A sign-in to tool connection {ConnectionId} failed: {Failure}", endpoint.ConnectionId, failure.Failure);
            return pending.Refused ? "The sign-in was not approved." : pending.Problem ?? failure.Message;
        }
        catch (OperationCanceledException ended)
        {
            logger.LogInformation("A sign-in to tool connection {ConnectionId} ended before it was finished: {Failure}", endpoint.ConnectionId, ended.GetType().Name);
            return pending.Refused ? "The sign-in was not approved." : pending.Problem ?? "The sign-in was not finished in time. Start it again.";
        }
        catch (Exception failure)
        {
            // Nothing of the failure is shown: it may hold what the tool server sent.
            logger.LogWarning("A sign-in to tool connection {ConnectionId} failed unexpectedly: {Failure}", endpoint.ConnectionId, failure.GetType().Name);
            return "The sign-in could not be finished.";
        }
        finally
        {
            await Forget(pending);
        }
    }

    /// <summary>
    /// Called by the tool source when the tool server wants the person. Stores the flow, lets the request that
    /// started the sign-in return the address, and waits here for the callback.
    /// </summary>
    private async Task<SignInAnswer> Ask(ToolEndpoint endpoint, Pending pending, SignInPrompt prompt, CancellationToken cancellationToken)
    {
        var address = prompt.AuthorizationUrl;
        var state = StateOf(address);

        // The address comes from the tool server's side and is opened in a person's browser. It must be a web page,
        // and it may be plain http only where the connection itself is.
        if (!address.IsAbsoluteUri || state is null || !(address.Scheme is "https" || (address.Scheme is "http" && endpoint.Url.Scheme is "http")))
        {
            pending.Problem = "The tool server named a sign-in page this server will not send a person to.";
            throw new OperationCanceledException(pending.Problem);
        }

        var hash = Hash(state);
        await pending.Tenant.Transact(
            services =>
            {
                services.GetRequiredService<ToolConnectionRepository>().Add(
                    new OAuthFlow
                    {
                        StateHash = hash,
                        AudienceUserId = pending.Tenant.UserId,
                        ToolConnectionId = pending.ConnectionId,
                        ExpiresAt = time.GetUtcNow() + FlowLifetime,
                    });
                return Task.CompletedTask;
            },
            cancellationToken);
        pending.StateHash = hash;
        byState[hash] = pending;
        pending.Asked.TrySetResult(address);
        return await pending.Answered.Task.WaitAsync(cancellationToken);
    }

    private async Task Forget(Pending pending)
    {
        byPerson.TryRemove(new KeyValuePair<(Guid, Guid), Pending>((pending.Tenant.UserId, pending.ConnectionId), pending));
        pending.Asked.TrySetCanceled();

        if (pending.StateHash is not { } hash)
        {
            return;
        }

        byState.TryRemove(new KeyValuePair<string, Pending>(hash, pending));

        try
        {
            await pending.Tenant.Transact(
                services => services.GetRequiredService<ToolConnectionRepository>().RemoveFlow(hash, CancellationToken.None),
                CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The row runs out by itself and is purged; a sign-in that worked must not fail on tidying up.
            logger.LogWarning("The record of a finished sign-in to tool connection {ConnectionId} could not be removed: {Failure}", pending.ConnectionId, failure.GetType().Name);
        }
    }

    /// <summary>Asks the database whose sign-in a state belongs to, with only the caller's user set: the callback has no space.</summary>
    private async Task<SignInInProgress?> Resolve(Guid userId, string hash, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(cancellationToken);
        var flow = await scope.ServiceProvider.GetRequiredService<ToolConnectionRepository>().ResolveFlow(hash, cancellationToken);
        await work.Commit(cancellationToken);
        return flow;
    }

    private static string? StateOf(Uri address) =>
        address.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == "state" && pair[1].Length > 0)
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();

    private static string Hash(string state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    #endregion

    #region Nested Types

    private sealed class Pending(RunTenant tenant, Guid connectionId)
    {
        #region Properties

        public RunTenant Tenant => tenant;

        public Guid ConnectionId => connectionId;

        public CancellationTokenSource Cancel { get; } = new(FlowLifetime);

        public TaskCompletionSource<Uri> Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<SignInAnswer> Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> Finished { get; set; } = Task.FromResult<string?>(null);

        public string? StateHash { get; set; }

        public bool Refused { get; set; }

        public string? Problem { get; set; }

        #endregion
    }

    #endregion
}
