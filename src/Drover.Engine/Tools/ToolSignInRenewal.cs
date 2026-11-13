namespace Drover.Engine.Tools;

/// <summary>
/// Renews sign-ins to tool servers before they run out, so a run that starts when nobody is there does not find a
/// sign-in that lapsed while it sat unused. A sign-in that can no longer be renewed is marked, so the person sees
/// "sign in again" on the connection before a run trips over it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Finding the sign-ins crosses spaces, so it goes through <c>sys.tool_sign_ins_running_out</c>, which returns
/// ids only. Each one is then renewed inside its own space's tenant scope, as the person it belongs to: the database
/// hands out that person's tokens and nobody else's, exactly as in a run. No transaction is open while the tool
/// server is asked.</item>
/// <item>One tool server that is down must not take every pass. A sign-in that was tried and not renewed is noted
/// and left out of the passes of the next half hour, and once one sign-in of a connection has failed for a reason
/// that is the server's, the rest of that connection's are noted and skipped in the same pass without being tried.</item>
/// <item>One sign-in that cannot even be prepared fails by itself; the pass goes on with the others.</item>
/// <item>How many a pass tries, how often a pass runs, and how far ahead it renews are deployment policy rules.</item>
/// </list>
/// </remarks>
public sealed class ToolSignInRenewal(
    RunScopes scopes,
    IEnumerable<IToolSource> sources,
    IOptions<DeploymentPolicyOptions> policy,
    TimeProvider time,
    ILogger<ToolSignInRenewal> logger)
{
    #region Private Fields

    /// <summary>How long a sign-in that was tried and not renewed waits before it is tried again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    /// <summary>How long one renewal may take. A pass is many of them, one after another.</summary>
    private static readonly TimeSpan patience = TimeSpan.FromSeconds(20);

    #endregion

    #region Properties

    /// <summary>The time between two passes, from the deployment policy.</summary>
    public TimeSpan Interval => TimeSpan.FromHours(1) / Math.Max(1, policy.Value.Rules.MaxToolSignInRenewalPassesPerHour);

    #endregion

    #region Public Methods

    /// <summary>
    /// Looks once for sign-ins that are running out and renews them. Returns how many now have new tokens: one
    /// that was looked at and needed nothing, or could not be renewed, is not counted.
    /// </summary>
    public async Task<int> RenewDue(CancellationToken cancellationToken)
    {
        var rules = policy.Value.Rules;

        if (sources.FirstOrDefault(found => found.Kind is ToolSourceKind.Mcp) is not { } source)
        {
            return 0;
        }

        var now = time.GetUtcNow();
        var renewBefore = now + TimeSpan.FromMinutes(rules.MaxToolSignInRenewalLeadMinutes);
        var due = await scopes.WithoutTenant(
            services => services.GetRequiredService<ToolConnectionRepository>()
                .ListSignInsRunningOut(renewBefore, now - RetryAfter, rules.MaxToolSignInRenewalsPerPass, cancellationToken),
            cancellationToken);
        HashSet<Guid> unanswering = [];
        var renewed = 0;

        foreach (var signIn in due)
        {
            try
            {
                if (unanswering.Contains(signIn.ToolConnectionId))
                {
                    // Its server did not answer a moment ago. Not tried, and it waits its turn like the one that was.
                    await NoteAttempt(signIn, cancellationToken);
                    continue;
                }

                switch (await Renew(source, signIn, renewBefore, cancellationToken))
                {
                    case Outcome.Renewed:
                        renewed++;
                        break;
                    case Outcome.ServerTrouble:
                        unanswering.Add(signIn.ToolConnectionId);
                        break;
                }
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // One sign-in that cannot be prepared, or noted, does not end the pass. Only the kind of failure is
                // logged: its text could hold what a tool server sent.
                logger.LogWarning(
                    "Renewing the sign-in of tool connection {ConnectionId} failed before anything was sent: {Failure}",
                    signIn.ToolConnectionId,
                    failure.GetType().Name);
            }
        }

        return renewed;
    }

    #endregion

    #region Private Methods

    private async Task<Outcome> Renew(IToolSource source, SignInRunningOut signIn, DateTimeOffset renewBefore, CancellationToken cancellationToken)
    {
        var tenant = scopes.For(signIn.SpaceId, signIn.AudienceUserId);

        // Noted before anything else, in a transaction of its own: whatever happens next, this one waits its turn.
        await NoteAttempt(signIn, cancellationToken);
        var (resolved, effective) = await tenant.Transact(
            async services =>
            {
                var connection = await services.GetRequiredService<ToolConnectionRepository>().FindConnection(signIn.ToolConnectionId, cancellationToken);
                return connection is null
                    ? (null, null)
                    : (
                        await services.GetRequiredService<ToolConnectionService>().Resolve(connection, signIn.AudienceUserId, cancellationToken),
                        (EffectivePolicy?)await services.GetRequiredService<PolicyService>().GetEffective(connection.SpaceId, cancellationToken));
            },
            cancellationToken);

        if (resolved?.Endpoint is not { SignIn: { } kept } endpoint || effective is null)
        {
            return Outcome.NothingToDo;
        }

        var stored = false;
        var watching = kept with
        {
            RenewBefore = renewBefore,
            WriteTokens = async (tokens, runsOutAt, cancellation) =>
            {
                await kept.WriteTokens(tokens, runsOutAt, cancellation);
                stored = true;
            },
        };

        try
        {
            using var attempt = OutboundAttempt.Begin(effective);

            // Listing is the cheapest request that changes nothing. The tool source renews the tokens on the way
            // and hands the new ones back through the same vault-backed functions a run uses, which also clear
            // the note made above.
            await source.ListTools(endpoint with { SignIn = watching, Timeout = patience }, cancellationToken);
            return stored ? Outcome.Renewed : Outcome.NothingToDo;
        }
        catch (ToolSourceException failure) when (failure.Failure is ToolSourceFailure.SignInRequired)
        {
            logger.LogInformation("The sign-in of tool connection {ConnectionId} can no longer be renewed", signIn.ToolConnectionId);
            await tenant.InScope(
                services => services.GetRequiredService<ToolConnectionService>().MarkNeedsReconnect(signIn.ToolConnectionId, signIn.CredentialId, cancellationToken));
            return Outcome.NothingToDo;
        }
        catch (ToolSourceException failure)
        {
            // The server may be down for a while. The sign-in is as good as it was; it is tried again later.
            logger.LogInformation("The sign-in of tool connection {ConnectionId} could not be renewed now: {Failure}", signIn.ToolConnectionId, failure.Failure);
            return failure.Failure is ToolSourceFailure.Unauthorized ? Outcome.NothingToDo : Outcome.ServerTrouble;
        }
    }

    private Task NoteAttempt(SignInRunningOut signIn, CancellationToken cancellationToken) =>
        scopes.For(signIn.SpaceId, signIn.AudienceUserId).Transact(
            async services =>
            {
                if (await services.GetRequiredService<ToolConnectionRepository>().GetCredential(signIn.CredentialId, cancellationToken) is { } credential)
                {
                    credential.RenewAttemptedAt = time.GetUtcNow();
                }
            },
            cancellationToken);

    #endregion

    #region Nested Types

    private enum Outcome
    {
        NothingToDo,
        Renewed,
        ServerTrouble,
    }

    #endregion
}
