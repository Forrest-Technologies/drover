namespace Drover.Engine.Tools;

/// <summary>
/// Renews sign-ins to tool servers before they run out, so a run that starts when nobody is there does not find a
/// sign-in that lapsed while it sat unused. A sign-in that can no longer be renewed is marked, so the person sees
/// "sign in again" on the connection before a run trips over it.
/// </summary>
/// <remarks>
/// Finding the sign-ins crosses spaces, so it goes through <c>sys.tool_sign_ins_running_out</c>, which returns ids
/// only. Each one is then renewed inside its own space's tenant scope, as the person it belongs to: the database
/// hands out that person's tokens and nobody else's, exactly as in a run. No transaction is open while the tool
/// server is asked.
/// </remarks>
public sealed class ToolSignInRenewal(
    RunScopes scopes,
    IEnumerable<IToolSource> sources,
    IOptions<ToolSignInOptions> options,
    TimeProvider time,
    ILogger<ToolSignInRenewal> logger)
{
    #region Public Methods

    /// <summary>Looks once for sign-ins that are running out and renews them. Returns how many were renewed.</summary>
    public async Task<int> RenewDue(CancellationToken cancellationToken)
    {
        if (sources.FirstOrDefault(found => found.Kind is ToolSourceKind.Mcp) is not { } source)
        {
            return 0;
        }

        var renewBefore = time.GetUtcNow() + options.Value.RenewAhead;
        var due = await scopes.WithoutTenant(
            services => services.GetRequiredService<ToolConnectionRepository>().ListSignInsRunningOut(renewBefore, options.Value.RenewAtMost, cancellationToken),
            cancellationToken);
        var renewed = 0;

        foreach (var signIn in due)
        {
            if (await Renew(source, signIn, renewBefore, cancellationToken))
            {
                renewed++;
            }
        }

        return renewed;
    }

    #endregion

    #region Private Methods

    private async Task<bool> Renew(IToolSource source, SignInRunningOut signIn, DateTimeOffset renewBefore, CancellationToken cancellationToken)
    {
        var tenant = scopes.For(signIn.SpaceId, signIn.AudienceUserId);
        var (resolved, policy) = await tenant.Transact(
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

        if (resolved?.Endpoint is not { SignIn: { } kept } endpoint || policy is null)
        {
            return false;
        }

        try
        {
            using var attempt = OutboundAttempt.Begin(policy);

            // Listing is the cheapest request that changes nothing. The tool source renews the tokens on the way
            // and hands the new ones back through the same vault-backed functions a run uses.
            await source.ListTools(endpoint with { SignIn = kept with { RenewBefore = renewBefore } }, cancellationToken);
            return true;
        }
        catch (ToolSourceException failure) when (failure.Failure is ToolSourceFailure.SignInRequired)
        {
            logger.LogInformation("The sign-in of tool connection {ConnectionId} can no longer be renewed", signIn.ToolConnectionId);
            await tenant.InScope(
                services => services.GetRequiredService<ToolConnectionService>().MarkNeedsReconnect(signIn.ToolConnectionId, signIn.CredentialId, cancellationToken));
            return false;
        }
        catch (ToolSourceException failure)
        {
            // The server may be down for a while. The sign-in is still running out, so the next look tries again.
            logger.LogInformation("The sign-in of tool connection {ConnectionId} could not be renewed now: {Failure}", signIn.ToolConnectionId, failure.Failure);
            return false;
        }
    }

    #endregion
}
