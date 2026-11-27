namespace Drover.Engine.Models;

/// <summary>
/// The one place that decides what a model call sends as its credential. For a key connection it is the
/// connection's key. For a sign-in connection it is the access token of the person the call acts for, read under
/// that person's tenant scope, and never anyone else's: a person who has not signed in has no credential, and there
/// is no fallback to the bot owner's sign-in, another member's, or a key (ADR 0006).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>Read</c> runs inside the caller's transaction, as the person the call acts for. It makes no
/// provider call.</item>
/// <item><see cref="Ready"/> runs after that transaction has closed. When the access token runs out within
/// <see cref="RefreshLead"/>, it renews it first: it takes the credential's refresh lock, reads the row again in a
/// short transaction (another run may have renewed it already), asks the provider with no transaction open, and
/// writes the new tokens in another short transaction before letting go. So two runs make one refresh between them,
/// and a refresh token is never sent twice.</item>
/// <item>A provider answer that the sign-in is over marks it <c>needs_sign_in</c> and keeps the row. Anything else
/// leaves the credential as it was, and the call fails as unreachable.</item>
/// <item>The deployment rule <c>SubscriptionSignIn</c> and the operator's <c>Drover:Models:SignInOff</c> are checked
/// on every use, before anything is read.</item>
/// </list>
/// </remarks>
public sealed class ModelCredentials(
    RunScopes scopes,
    IEnumerable<IModelSignIn> signIns,
    ModelProviderCatalog catalog,
    ModelCredentialLocks locks,
    IOptions<DeploymentPolicyOptions> deployment,
    IOptions<ModelSignInOptions> options,
    TimeProvider time,
    ILogger<ModelCredentials> logger)
{
    #region Private Fields

    /// <summary>An access token that runs out sooner than this is renewed before it is used.</summary>
    public static readonly TimeSpan RefreshLead = TimeSpan.FromMinutes(5);

    /// <summary>What a refused sign-in kind is told, on every route and every use.</summary>
    public const string NotAvailable = "Signing in with a personal plan is not available on this server. You can add an API key instead.";

    private static readonly JsonSerializerOptions tokenText = new(JsonSerializerDefaults.Web);

    #endregion

    #region Public Methods

    /// <summary>The sign-in of a kind, or null when the kind is not one each person signs in to.</summary>
    public IModelSignIn? FindSignIn(string kind) => signIns.FirstOrDefault(candidate => candidate.Kind == kind);

    public bool IsSignInKind(string kind) => FindSignIn(kind) is not null;

    /// <summary>
    /// Why this server refuses a sign-in kind, or null when it allows it. A sign-in kind is a provider kind too, so
    /// the provider catalog decides first: a kind with no provider registered through it, or one the deployment does
    /// not offer, is refused with the catalog's sentence. Then a personal plan kind under the hosted preset, or a kind
    /// the operator switched off with <c>Drover:Models:SignInOff</c>.
    /// </summary>
    public string? FindRefusal(IModelSignIn signIn)
    {
        if (catalog.Resolve(signIn.Kind).Refusal is { } notOffered)
        {
            return notOffered;
        }

        var switchedOff = options.Value.SignInOff.Contains(signIn.Kind, StringComparer.OrdinalIgnoreCase);
        var plansRefused = deployment.Value.Rules.SubscriptionSignIn is SubscriptionSignInMode.OnlyWithOperatorApproval && signIn.IsPersonalPlan;
        return switchedOff || plansRefused ? NotAvailable : null;
    }

    /// <summary>
    /// The credential for a call through the connection, read inside the caller's transaction as the person the call
    /// acts for. A sign-in comes back with what <see cref="Ready"/> needs to renew it when it runs out soon.
    /// </summary>
    public Task<ModelCredential> Read(IServiceProvider services, ModelConnection connection, CancellationToken cancellationToken) =>
        Read(
            services.GetRequiredService<TenantContext>(),
            services.GetRequiredService<ModelConnectionCredentialRepository>(),
            services.GetRequiredService<ISecretVault>(),
            connection,
            cancellationToken);

    /// <summary>As the other overload, with the scope's own services named.</summary>
    public async Task<ModelCredential> Read(
        TenantContext tenant,
        ModelConnectionCredentialRepository signIns,
        ISecretVault vault,
        ModelConnection connection,
        CancellationToken cancellationToken)
    {
        if (FindSignIn(connection.Kind) is not { } signIn)
        {
            return await ReadKey(vault, connection, cancellationToken);
        }

        if (FindRefusal(signIn) is { } refusal)
        {
            return ModelCredential.Refused(ModelCredentialProblem.NotAvailable, refusal);
        }

        if (tenant.UserId is not { } userId || userId == Audience.Everyone)
        {
            return Missing(connection);
        }

        var credential = await signIns.GetCredential(connection.Id, userId, cancellationToken);

        if (credential is null)
        {
            return Missing(connection);
        }

        if (credential.Status is ModelSignInStatus.NeedsSignIn)
        {
            // A row kept after a sign-out holds no account label and no token: the person is not signed in.
            return credential.AccountLabel is null ? Missing(connection) : Ended(connection.Name);
        }

        if (await RevealTokens(vault, credential, cancellationToken) is not { } tokens)
        {
            return Unreadable(connection.Name);
        }

        return SignedIn(tokens, new(connection.SpaceId, userId, connection.Id, credential.Id, connection.Kind, connection.Name, credential.SecretId));
    }

    /// <summary>
    /// The credential ready to send. Renews a sign-in that runs out soon first. Call it with no transaction open:
    /// a renewal waits on the provider. A call that lasts calls it again before each model call, so a token that runs
    /// out while a run goes on is renewed then.
    /// </summary>
    public async Task<ModelCredential> Ready(ModelCredential credential, CancellationToken cancellationToken)
    {
        if (credential is not { SignIn: { } handle, Problem: null } || !RunsOutSoon(credential.ExpiresAt))
        {
            return credential;
        }

        return await Renew(handle, (tokens, _) => RunsOutSoon(tokens.ExpiresAt), cancellationToken);
    }

    /// <summary>
    /// Renews one sign-in under its refresh lock when <paramref name="isDue"/> still holds for what is stored once the
    /// lock is held (the tokens, and when they were last renewed), and returns the credential to send. The background
    /// renewal uses it as a call does.
    /// </summary>
    public async Task<ModelCredential> Renew(
        ModelSignInHandle handle,
        Func<ModelTokens, DateTimeOffset?, bool> isDue,
        CancellationToken cancellationToken)
    {
        if (TenantScope.IsOpenInCurrentFlow)
        {
            throw new InvalidOperationException("A sign-in was renewed inside a tenant transaction. Close the scope before the call.");
        }

        if (FindSignIn(handle.Kind) is not { } signIn)
        {
            return Missing(handle.ConnectionName);
        }

        if (FindRefusal(signIn) is { } refusal)
        {
            return ModelCredential.Refused(ModelCredentialProblem.NotAvailable, refusal);
        }

        await using var held = await locks.Hold(handle.CredentialId, cancellationToken);
        var tenant = scopes.For(handle.SpaceId, handle.UserId);
        var stored = await tenant.Transact(services => ReadStored(services, handle, cancellationToken), cancellationToken);

        if (stored.Problem is not null || stored.Tokens is not { } tokens)
        {
            return stored.Problem ?? Missing(handle.ConnectionName);
        }

        if (!isDue(tokens, stored.RenewedAt))
        {
            // Another call renewed it while this one waited for the lock.
            return SignedIn(tokens, handle);
        }

        ModelTokens renewed;

        try
        {
            using var outbound = OutboundAttempt.Begin(stored.Policy!);
            renewed = await signIn.Refresh(tokens, cancellationToken);
        }
        catch (SignInEndedException)
        {
            logger.LogInformation("The sign-in {CredentialId} to model connection {ConnectionId} has ended", handle.CredentialId, handle.ConnectionId);
            await tenant.Transact(services => MarkEnded(services, handle, stored.SecretId, CancellationToken.None), CancellationToken.None);
            return Ended(handle.ConnectionName);
        }
        catch (Exception failure) when (IsTransient(failure, cancellationToken))
        {
            // Only the kind of failure: its text could hold what the provider sent.
            logger.LogWarning(
                "The sign-in {CredentialId} to model connection {ConnectionId} could not be renewed now: {Failure}",
                handle.CredentialId,
                handle.ConnectionId,
                failure.GetType().Name);
            return ModelCredential.Refused(
                ModelCredentialProblem.Unreachable,
                $"{handle.ConnectionName} could not renew your sign-in just now. Try again shortly.");
        }

        // The provider has rotated the tokens and the old refresh token is spent: the new set is kept whatever
        // happens to the call now, or the next refresh would send a spent token and end the sign-in.
        return await tenant.Transact(services => StoreRenewed(services, handle, stored.SecretId, renewed, CancellationToken.None), CancellationToken.None);
    }

    /// <summary>
    /// A call with this credential was refused as having an invalid key. For a sign-in whose credential has no end (a
    /// key the provider made, which the person can delete or let lapse) that is the provider saying the sign-in is
    /// over: it is marked <c>needs_sign_in</c> and the call ends as an ended sign-in, not as a key to check. A
    /// sign-in with an end is left alone, because renewing it may mend it. Call it with no transaction open.
    /// </summary>
    public async Task<ModelCredential?> EndAfterRejection(ModelCredential credential, CancellationToken cancellationToken)
    {
        if (credential is not { SignIn: { SecretId: { } secretId } handle, ExpiresAt: null })
        {
            return null;
        }

        logger.LogInformation("The provider refused the key of sign-in {CredentialId} to model connection {ConnectionId}", handle.CredentialId, handle.ConnectionId);
        await scopes.For(handle.SpaceId, handle.UserId).Transact(services => MarkEnded(services, handle, secretId, CancellationToken.None), CancellationToken.None);
        return Ended(handle.ConnectionName);
    }

    /// <summary>The token set as it is kept in the vault.</summary>
    public static string Write(ModelTokens tokens) => JsonSerializer.Serialize(tokens, tokenText);

    /// <summary>A token set kept in the vault. Throws <see cref="JsonException"/> when the text is not one.</summary>
    public static ModelTokens? ReadTokens(string text) => JsonSerializer.Deserialize<ModelTokens>(text, tokenText);

    /// <summary>The person's own token set, read in their transaction, or null when it cannot be read.</summary>
    public async Task<ModelTokens?> RevealTokens(ISecretVault vault, ModelConnectionCredential credential, CancellationToken cancellationToken)
    {
        try
        {
            return ReadTokens(await vault.Reveal(credential.SecretId, cancellationToken));
        }
        catch (Exception unreadable) when (unreadable is CryptographicException or KeyNotFoundException or JsonException)
        {
            // Only the kind of failure: a parser's message could quote what it read.
            logger.LogError(
                "The sign-in {CredentialId} to model connection {ConnectionId} could not be read: {Failure}",
                credential.Id,
                credential.ModelConnectionId,
                unreadable.GetType().Name);
            return null;
        }
    }

    #endregion

    #region Private Methods

    private async Task<ModelCredential> ReadKey(ISecretVault vault, ModelConnection connection, CancellationToken cancellationToken)
    {
        if (connection.SecretId is not { } secretId)
        {
            return ModelCredential.None;
        }

        try
        {
            return ModelCredential.Of(await vault.Reveal(secretId, cancellationToken));
        }
        catch (Exception unreadable) when (unreadable is CryptographicException or KeyNotFoundException)
        {
            logger.LogError(unreadable, "The key of model connection {ConnectionId} could not be read", connection.Id);
            return ModelCredential.Refused(
                ModelCredentialProblem.Unreadable,
                "The key of the model connection could not be read. Save the key on the connection again.");
        }
    }

    private async Task<StoredSignIn> ReadStored(IServiceProvider services, ModelSignInHandle handle, CancellationToken cancellationToken)
    {
        var credential = await services.GetRequiredService<ModelConnectionCredentialRepository>().GetCredential(handle.CredentialId, cancellationToken);

        if (credential is null || credential.ModelConnectionId != handle.ConnectionId || credential.AudienceUserId != handle.UserId)
        {
            return new StoredSignIn(null, Guid.Empty, null, null, Missing(handle.ConnectionName));
        }

        if (credential.Status is ModelSignInStatus.NeedsSignIn)
        {
            return new StoredSignIn(null, credential.SecretId, null, null, Ended(handle.ConnectionName));
        }

        if (await RevealTokens(services.GetRequiredService<ISecretVault>(), credential, cancellationToken) is not { } tokens)
        {
            return new StoredSignIn(null, credential.SecretId, null, null, Unreadable(handle.ConnectionName));
        }

        var policy = await services.GetRequiredService<PolicyService>().GetEffective(handle.SpaceId, cancellationToken);
        return new StoredSignIn(tokens, credential.SecretId, credential.RenewedAt, policy, null);
    }

    /// <summary>
    /// Writes the renewed tokens, unless the person signed in again while the provider answered: then the new
    /// sign-in's tokens stand, and the call uses them.
    /// </summary>
    private async Task<ModelCredential> StoreRenewed(
        IServiceProvider services,
        ModelSignInHandle handle,
        Guid secretId,
        ModelTokens renewed,
        CancellationToken cancellationToken)
    {
        var credential = await services.GetRequiredService<ModelConnectionCredentialRepository>().GetCredential(handle.CredentialId, cancellationToken);

        if (credential is null)
        {
            return Missing(handle.ConnectionName);
        }

        if (credential.SecretId != secretId)
        {
            return await RevealTokens(services.GetRequiredService<ISecretVault>(), credential, cancellationToken) is { } current
                ? SignedIn(current, handle with { SecretId = credential.SecretId })
                : Unreadable(handle.ConnectionName);
        }

        await services.GetRequiredService<ISecretVault>().Replace(secretId, Write(renewed), cancellationToken);
        credential.Status = ModelSignInStatus.Active;
        credential.ExpiresAt = renewed.ExpiresAt;
        credential.RenewedAt = time.GetUtcNow();
        credential.RenewAttemptedAt = null;
        credential.AccountLabel = renewed.AccountLabel ?? credential.AccountLabel;
        logger.LogInformation("The sign-in {CredentialId} to model connection {ConnectionId} was renewed", handle.CredentialId, handle.ConnectionId);
        return SignedIn(renewed, handle);
    }

    private static async Task MarkEnded(IServiceProvider services, ModelSignInHandle handle, Guid secretId, CancellationToken cancellationToken)
    {
        var credential = await services.GetRequiredService<ModelConnectionCredentialRepository>().GetCredential(handle.CredentialId, cancellationToken);

        // A sign-in made again while the provider answered is not the one that ended.
        if (credential is not null && credential.SecretId == secretId)
        {
            credential.Status = ModelSignInStatus.NeedsSignIn;
        }
    }

    private bool RunsOutSoon(DateTimeOffset? expiresAt) =>
        expiresAt is { } end && end - time.GetUtcNow() < RefreshLead;

    private static ModelCredential SignedIn(ModelTokens tokens, ModelSignInHandle handle) =>
        ModelCredential.Of(tokens.AccessToken) with { SignIn = handle, ExpiresAt = tokens.ExpiresAt, Details = tokens.Details };

    private static bool IsTransient(Exception failure, CancellationToken cancellationToken) =>
        failure is ModelProviderException or HttpRequestException or IOException
        || (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static ModelCredential Missing(ModelConnection connection) => Missing(connection.Name);

    private static ModelCredential Missing(string connectionName) =>
        ModelCredential.Refused(
            ModelCredentialProblem.SignInMissing,
            $"{connectionName} answers with each person's own account. Sign in with yours to use it.");

    private static ModelCredential Ended(string connectionName) =>
        ModelCredential.Refused(ModelCredentialProblem.SignInEnded, $"Your {connectionName} sign-in has ended. Sign in again.");

    private static ModelCredential Unreadable(string connectionName) =>
        ModelCredential.Refused(ModelCredentialProblem.Unreadable, $"Your {connectionName} sign-in could not be read. Sign in again.");

    #endregion

    #region Nested Types

    private sealed record StoredSignIn(ModelTokens? Tokens, Guid SecretId, DateTimeOffset? RenewedAt, EffectivePolicy? Policy, ModelCredential? Problem);

    #endregion
}
