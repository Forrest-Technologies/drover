namespace Drover.Engine.Models;

/// <summary>Where a model sign-in stands, as the person who started it reads it.</summary>
public enum ModelSignInState
{
    Waiting,
    Done,
    Refused,
    Expired,
}

/// <summary>What starting a sign-in came to: where the person goes, or a sentence and the status code that says why not.</summary>
public sealed record ModelSignInBegun(Guid AttemptId, Uri? Address, string? Code, ModelSignInWait Wait, string? Problem, int Status)
{
    public static ModelSignInBegun Refused(string problem, int status) => new(Guid.Empty, null, null, ModelSignInWait.Return, problem, status);
}

/// <summary>
/// What finishing or reading a sign-in came to. A null <see cref="State"/> means no attempt of the caller's has that id.
/// <see cref="ConnectionId"/> is set once the sign-in is done.
/// </summary>
public sealed record ModelSignInOutcome(ModelSignInState? State, Guid? ConnectionId, string? Problem, int Status)
{
    public static ModelSignInOutcome NotFound { get; } = new(null, null, null, 404);
}

/// <summary>
/// Starts and finishes people's sign-ins to model providers with their own accounts. The token exchange runs here,
/// on the server; the person's device only ever carries the one-time code back.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A pending attempt (its PKCE verifier, state, nonce, person, space, kind and return address) lives in this
/// process for <see cref="AttemptLifetime"/>, as a tool sign-in's does. One person has at most one per kind in a
/// space; starting another ends the first. Only the person who started it, in the same space, can finish it or read
/// it: anyone else is told it does not exist.</item>
/// <item>On finish the token set goes to the vault, private to the person, and the space's one connection of the
/// kind is made if there is none. Its models are listed once, as a refresh lists them.</item>
/// <item>The deployment rule <c>SubscriptionSignIn</c> and <c>Drover:Models:SignInOff</c> are checked before any
/// request leaves, on every step.</item>
/// <item>No transaction is open while the provider is asked.</item>
/// </list>
/// </remarks>
public sealed class ModelSignIns(
    RunScopes scopes,
    ModelCredentials credentials,
    TimeProvider time,
    ILogger<ModelSignIns> logger)
{
    #region Private Fields

    /// <summary>How long a person has to approve and come back.</summary>
    public static readonly TimeSpan AttemptLifetime = TimeSpan.FromMinutes(10);

    private const string UnknownKind = "That kind of sign-in is not available on this server.";

    private const string WrongReturn = "This device cannot receive the sign-in's return at that address.";

    private const string TooLong = "Sign-in took too long.";

    private const string Cancelled = "Sign-in was cancelled.";

    private readonly ConcurrentDictionary<Guid, Pending> attempts = new();

    #endregion

    #region Public Methods

    /// <summary>Starts a sign-in to a kind for the person in the space, and says where they go to approve it.</summary>
    public async Task<ModelSignInBegun> Begin(Guid spaceId, Guid userId, string kind, Uri? redirectUri, CancellationToken cancellationToken)
    {
        if (credentials.FindSignIn(kind) is not { } signIn)
        {
            return ModelSignInBegun.Refused(UnknownKind, 400);
        }

        if (credentials.FindRefusal(signIn) is { } refusal)
        {
            return ModelSignInBegun.Refused(refusal, 403);
        }

        if (!signIn.AcceptsRedirect(redirectUri))
        {
            return ModelSignInBegun.Refused(WrongReturn, 400);
        }

        ForgetExpired();
        var attempt = new ModelSignInAttempt(Guid.CreateVersion7(), Random(), Random(), Random(), redirectUri);

        // One sign-in per person and kind in a space: starting another ends the first.
        foreach (var (id, earlier) in attempts)
        {
            if (earlier.SpaceId == spaceId && earlier.UserId == userId && earlier.Kind == kind)
            {
                attempts.TryRemove(id, out _);
            }
        }

        var step = await signIn.Begin(attempt, cancellationToken);
        attempts[attempt.Id] = new Pending(attempt, spaceId, userId, kind, time.GetUtcNow());
        logger.LogInformation("Model sign-in {AttemptId} to {Kind} started in space {SpaceId}", attempt.Id, kind, spaceId);
        return new ModelSignInBegun(attempt.Id, step.Address, step.Code, step.Wait, null, 200);
    }

    /// <summary>
    /// Takes what the person's device received and finishes the sign-in: exchanges it, stores the tokens privately
    /// to the person, makes the space's connection of the kind if there is none, and lists its models.
    /// </summary>
    public async Task<ModelSignInOutcome> Finish(Guid spaceId, Guid userId, Guid attemptId, string? arrived, CancellationToken cancellationToken)
    {
        if (Find(spaceId, userId, attemptId) is not { } pending)
        {
            return ModelSignInOutcome.NotFound;
        }

        if (StateOf(pending) is not ModelSignInState.Waiting || !pending.TryStartFinishing())
        {
            return Outcome(pending, StateOf(pending) is ModelSignInState.Expired ? TooLong : "This sign-in is not waiting any more. Start it again.", 400);
        }

        if (credentials.FindSignIn(pending.Kind) is not { } signIn || credentials.FindRefusal(signIn) is { } refusal)
        {
            pending.End(ModelSignInState.Refused);
            return Outcome(pending, credentials.FindSignIn(pending.Kind) is null ? UnknownKind : ModelCredentials.NotAvailable, 403);
        }

        var tenant = scopes.For(spaceId, userId);
        var policy = await tenant.Transact(services => services.GetRequiredService<PolicyService>().GetEffective(spaceId, cancellationToken), cancellationToken);
        var tokens = await Exchange(signIn, pending, policy, arrived ?? string.Empty, cancellationToken);

        if (tokens is null)
        {
            return Outcome(pending, Cancelled, 400);
        }

        var connectionId = await tenant.Transact(services => Store(services, pending, signIn, tokens, cancellationToken), CancellationToken.None);
        await ListModels(tenant, connectionId, cancellationToken);
        pending.Done(connectionId);
        logger.LogInformation("Model sign-in {AttemptId} to {Kind} finished for connection {ConnectionId}", attemptId, pending.Kind, connectionId);
        return Outcome(pending, null, 200);
    }

    /// <summary>Where the caller's own sign-in stands. Anyone else's, or one in another space, does not exist for them.</summary>
    public ModelSignInOutcome Read(Guid spaceId, Guid userId, Guid attemptId) =>
        Find(spaceId, userId, attemptId) is { } pending ? Outcome(pending, null, 200) : ModelSignInOutcome.NotFound;

    /// <summary>
    /// Signs the caller out of a connection: tells the provider where it can be told, then deletes the token set and
    /// the row. False when the caller has no sign-in to it. Where this server refuses the kind, nothing is sent and the
    /// sign-in is still removed, so a person can always take their own tokens off the server.
    /// </summary>
    public async Task<bool> SignOut(Guid spaceId, Guid userId, Guid connectionId, CancellationToken cancellationToken)
    {
        var tenant = scopes.For(spaceId, userId);
        var found = await tenant.Transact(services => FindOwn(services, connectionId, userId, cancellationToken), cancellationToken);

        if (found is null)
        {
            return false;
        }

        if (found.SignIn is { } signIn && credentials.FindRefusal(signIn) is null && found.Tokens is { } tokens)
        {
            await Revoke(signIn, found.Policy!, tokens, connectionId, cancellationToken);
        }

        await tenant.Transact(services => Forget(services, connectionId, userId, cancellationToken), CancellationToken.None);
        logger.LogInformation("Signed out of model connection {ConnectionId}", connectionId);
        return true;
    }

    #endregion

    #region Finishing

    /// <summary>The provider's exchange, with no transaction open. Null when the attempt ended without tokens.</summary>
    private async Task<ModelTokens?> Exchange(IModelSignIn signIn, Pending pending, EffectivePolicy policy, string arrived, CancellationToken cancellationToken)
    {
        try
        {
            using var outbound = OutboundAttempt.Begin(policy);
            var tokens = await signIn.Finish(pending.Attempt, new ModelSignInReturn(arrived), cancellationToken);

            if (tokens is null)
            {
                pending.StopFinishing();
            }

            return tokens;
        }
        catch (Exception failure) when (failure is ModelSignInRefusedException or ModelProviderException or HttpRequestException or IOException)
        {
            // Only the kind of failure: its text could hold what the provider sent.
            logger.LogInformation("Model sign-in {AttemptId} to {Kind} ended without tokens: {Failure}", pending.Attempt.Id, pending.Kind, failure.GetType().Name);
            pending.End(ModelSignInState.Refused);
            return null;
        }
    }

    /// <summary>
    /// Keeps the token set, privately to the person, as a new secret, and makes the space's connection of the kind
    /// if there is none. A person who signs in again gets a new secret and the old one is deleted, so a renewal still
    /// waiting on the provider can tell its tokens were replaced.
    /// </summary>
    private async Task<Guid> Store(IServiceProvider services, Pending pending, IModelSignIn signIn, ModelTokens tokens, CancellationToken cancellationToken)
    {
        var connections = services.GetRequiredService<ModelConnectionRepository>();
        var signIns = services.GetRequiredService<ModelConnectionCredentialRepository>();
        var vault = services.GetRequiredService<ISecretVault>();

        await connections.LockKind(pending.Kind, cancellationToken);
        var connection = await connections.FindConnectionOfKind(pending.Kind, cancellationToken);

        if (connection is null)
        {
            connection = new ModelConnection { Name = signIn.DisplayName, Kind = pending.Kind, Status = ConnectionStatus.Ready, LastCheckedAt = time.GetUtcNow() };
            connections.Add(connection);
        }

        var secretId = await vault.Store(SecretKind.OauthTokens, ModelCredentials.Write(tokens), pending.UserId, cancellationToken);
        var credential = await signIns.GetCredential(connection.Id, pending.UserId, cancellationToken);

        if (credential is null)
        {
            signIns.Add(new ModelConnectionCredential
            {
                ModelConnectionId = connection.Id,
                AudienceUserId = pending.UserId,
                SecretId = secretId,
                AccountLabel = tokens.AccountLabel,
                ExpiresAt = tokens.ExpiresAt,
                RenewedAt = time.GetUtcNow(),
            });
            return connection.Id;
        }

        var previous = credential.SecretId;
        credential.SecretId = secretId;
        credential.Status = ModelSignInStatus.Active;
        credential.AccountLabel = tokens.AccountLabel;
        credential.ExpiresAt = tokens.ExpiresAt;
        credential.RenewedAt = time.GetUtcNow();
        await vault.Delete(previous, cancellationToken);
        return connection.Id;
    }

    /// <summary>
    /// Lists the connection's models once, as the person who signed in, so the Models screen has them at once. A
    /// listing that fails leaves the sign-in done; the person can refresh later.
    /// </summary>
    private async Task ListModels(RunTenant tenant, Guid connectionId, CancellationToken cancellationToken)
    {
        try
        {
            await tenant.InScope(services => services.GetRequiredService<ModelConnectionService>().RefreshModels(connectionId, cancellationToken));
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning("The models of connection {ConnectionId} could not be listed after a sign-in: {Failure}", connectionId, failure.GetType().Name);
        }
    }

    #endregion

    #region Signing Out

    private async Task<OwnSignIn?> FindOwn(IServiceProvider services, Guid connectionId, Guid userId, CancellationToken cancellationToken)
    {
        var connection = await services.GetRequiredService<ModelConnectionRepository>().FindConnection(connectionId, cancellationToken);
        var credential = await services.GetRequiredService<ModelConnectionCredentialRepository>().GetCredential(connectionId, userId, cancellationToken);

        if (connection is null || credential is null)
        {
            return null;
        }

        var tokens = await credentials.RevealTokens(services.GetRequiredService<ISecretVault>(), credential, cancellationToken);
        var policy = await services.GetRequiredService<PolicyService>().GetEffective(connection.SpaceId, cancellationToken);
        return new OwnSignIn(credentials.FindSignIn(connection.Kind), tokens, policy);
    }

    /// <summary>Tells the provider, with no transaction open. A provider that cannot be told does not keep the person signed in.</summary>
    private async Task Revoke(IModelSignIn signIn, EffectivePolicy policy, ModelTokens tokens, Guid connectionId, CancellationToken cancellationToken)
    {
        try
        {
            using var outbound = OutboundAttempt.Begin(policy);
            await signIn.Revoke(tokens, cancellationToken);
        }
        catch (Exception failure) when (failure is ModelProviderException or SignInEndedException or HttpRequestException or IOException)
        {
            logger.LogInformation("The provider of connection {ConnectionId} could not be told of a sign-out: {Failure}", connectionId, failure.GetType().Name);
        }
    }

    private static async Task Forget(IServiceProvider services, Guid connectionId, Guid userId, CancellationToken cancellationToken)
    {
        var signIns = services.GetRequiredService<ModelConnectionCredentialRepository>();

        if (await signIns.GetCredential(connectionId, userId, cancellationToken) is { } credential)
        {
            signIns.Remove(credential);
            await services.GetRequiredService<ISecretVault>().Delete(credential.SecretId, cancellationToken);
        }
    }

    #endregion

    #region Attempts

    private Pending? Find(Guid spaceId, Guid userId, Guid attemptId) =>
        attempts.TryGetValue(attemptId, out var pending) && pending.SpaceId == spaceId && pending.UserId == userId ? pending : null;

    private ModelSignInState StateOf(Pending pending) =>
        pending.State is ModelSignInState.Waiting && time.GetUtcNow() - pending.StartedAt > AttemptLifetime
            ? ModelSignInState.Expired
            : pending.State;

    private ModelSignInOutcome Outcome(Pending pending, string? problem, int status) =>
        new(StateOf(pending), pending.ConnectionId, problem, status);

    /// <summary>Forgets attempts older than their lifetime, so the ones a person never came back from do not pile up.</summary>
    private void ForgetExpired()
    {
        var now = time.GetUtcNow();

        foreach (var (id, pending) in attempts)
        {
            if (now - pending.StartedAt > AttemptLifetime)
            {
                attempts.TryRemove(id, out _);
            }
        }
    }

    private static string Random() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    #endregion

    #region Nested Types

    private sealed record OwnSignIn(IModelSignIn? SignIn, ModelTokens? Tokens, EffectivePolicy Policy);

    /// <summary>One sign-in waiting for the person. Its state changes once, from waiting, under its own lock.</summary>
    private sealed class Pending(ModelSignInAttempt attempt, Guid spaceId, Guid userId, string kind, DateTimeOffset startedAt)
    {
        #region Private Fields

        private readonly Lock sync = new();

        private bool finishing;

        #endregion

        #region Properties

        public ModelSignInAttempt Attempt => attempt;

        public Guid SpaceId => spaceId;

        public Guid UserId => userId;

        public string Kind => kind;

        public DateTimeOffset StartedAt => startedAt;

        public ModelSignInState State { get; private set; } = ModelSignInState.Waiting;

        public Guid? ConnectionId { get; private set; }

        #endregion

        #region Public Methods

        /// <summary>True for the one finish that may exchange; a second at the same time is turned away.</summary>
        public bool TryStartFinishing()
        {
            lock (sync)
            {
                if (finishing || State is not ModelSignInState.Waiting)
                {
                    return false;
                }

                finishing = true;
                return true;
            }
        }

        public void StopFinishing()
        {
            lock (sync)
            {
                finishing = false;
            }
        }

        public void End(ModelSignInState state)
        {
            lock (sync)
            {
                State = state;
            }
        }

        public void Done(Guid connectionId)
        {
            lock (sync)
            {
                ConnectionId = connectionId;
                State = ModelSignInState.Done;
            }
        }

        #endregion
    }

    #endregion
}
