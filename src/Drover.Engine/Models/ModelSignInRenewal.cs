namespace Drover.Engine.Models;

/// <summary>
/// Renews people's model sign-ins that have sat unused, so a refresh token that would lapse from disuse is used
/// before it does. A call renews a sign-in whose access token runs out soon by itself; this pass covers the ones no
/// call has touched for <see cref="Idle"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Finding them crosses spaces, so it goes through <c>sys.model_sign_ins_due</c>, which returns ids only. Each
/// one is then renewed inside its own space's tenant scope, as the person it belongs to, through
/// <see cref="ModelCredentials.Renew"/> and its refresh lock, exactly as a call renews it. No transaction is open
/// while the provider is asked.</item>
/// <item>A sign-in with no end, or whose kind this server refuses, is left alone: nothing is sent for it.</item>
/// <item>One sign-in that fails does not end the pass.</item>
/// </list>
/// </remarks>
public sealed class ModelSignInRenewal(
    RunScopes scopes,
    ModelCredentials credentials,
    TimeProvider time,
    ILogger<ModelSignInRenewal> logger)
{
    #region Private Fields

    /// <summary>A sign-in not renewed for this long is renewed by the pass.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromDays(7);

    /// <summary>
    /// How long a sign-in that a pass took up and did not renew waits before another pass takes it up: one that
    /// failed, or one passed over because this server refuses its kind. So those cannot fill every pass.
    /// </summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromDays(1);

    /// <summary>The time between two passes.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>The most sign-ins one pass renews. Each is a request to a provider made on this server's own initiative.</summary>
    public const int MaxPerPass = 50;

    #endregion

    #region Public Methods

    /// <summary>Looks once for sign-ins idle for <see cref="Idle"/> and renews them. Returns how many now have new tokens.</summary>
    public async Task<int> RenewDue(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var cutoff = now - Idle;
        var due = await scopes.WithoutTenant(
            services => services.GetRequiredService<ModelConnectionCredentialRepository>().ListSignInsDue(cutoff, now - RetryAfter, MaxPerPass, cancellationToken),
            cancellationToken);
        var renewed = 0;

        foreach (var signIn in due)
        {
            try
            {
                if (await Renew(signIn, cutoff, cancellationToken))
                {
                    renewed++;
                }
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // Only the kind of failure: its text could hold what a provider sent.
                logger.LogWarning(
                    "Renewing the sign-in {CredentialId} to model connection {ConnectionId} failed: {Failure}",
                    signIn.CredentialId,
                    signIn.ModelConnectionId,
                    failure.GetType().Name);
            }
        }

        return renewed;
    }

    #endregion

    #region Private Methods

    private async Task<bool> Renew(ModelSignInDue signIn, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        // Noted first, in its own transaction, whatever happens next: a renewal clears it, and anything else leaves
        // the sign-in to wait its turn.
        var connection = await scopes.For(signIn.SpaceId, signIn.AudienceUserId).Transact(
            async services =>
            {
                if (await services.GetRequiredService<ModelConnectionCredentialRepository>().GetCredential(signIn.CredentialId, cancellationToken) is { } credential)
                {
                    credential.RenewAttemptedAt = time.GetUtcNow();
                }

                return await services.GetRequiredService<ModelConnectionRepository>().FindConnection(signIn.ModelConnectionId, cancellationToken);
            },
            cancellationToken);

        if (connection is null || credentials.FindSignIn(connection.Kind) is not { } kind || credentials.FindRefusal(kind) is not null)
        {
            return false;
        }

        var handle = new ModelSignInHandle(signIn.SpaceId, signIn.AudienceUserId, connection.Id, signIn.CredentialId, connection.Kind, connection.Name);
        var stillIdle = false;
        var result = await credentials.Renew(
            handle,
            (_, renewedAt) => stillIdle = renewedAt is null || renewedAt < cutoff,
            cancellationToken);
        return result.Problem is null && stillIdle;
    }

    #endregion
}
