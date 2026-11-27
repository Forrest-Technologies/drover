namespace Drover.Engine.Decisions;

/// <summary>
/// Finds a space's decision model for a rule check. It reads the connection and its key in a scope of its own, as the
/// person the check is for, closes the scope, and builds a client. Only a connection that passed its last test, of a
/// kind this server still offers, with an address it can reach, is handed over; anything else is no model, so every
/// rule written as a sentence is unsure. This is the server's <see cref="FindDecisionModel"/>.
/// </summary>
public sealed class SpaceDecisionModels(
    IServiceScopeFactory scopes,
    IEnumerable<IDecisionModelProvider> providers,
    IOptions<DeploymentPolicyOptions> deployment,
    ILogger<SpaceDecisionModels> logger)
{
    #region Public Methods

    public async Task<SpaceDecisionModel?> Find(Guid spaceId, Guid userId, CancellationToken cancellationToken)
    {
        if (await Read(spaceId, userId, cancellationToken) is not { } read)
        {
            return null;
        }

        var (connection, key) = read;

        if (!deployment.Value.Rules.OffersDecisionModel(connection.Kind)
            || providers.FirstOrDefault(provider => provider.Kind == connection.Kind) is not { } provider
            || provider.AddressFor(connection.Address, connection.Account) is not { } address)
        {
            logger.LogInformation("The decision model of space {SpaceId} is not offered or cannot be reached; rules written as a sentence are unsure", spaceId);
            return null;
        }

        return new SpaceDecisionModel(provider.CreateClient(new ModelEndpoint(provider.Kind, address), key, connection.Model), connection.Model, connection.Id)
        {
            Pricing = new ModelPricing { InputPerMillion = connection.InputPricePerMillion },
        };
    }

    #endregion

    #region Private Methods

    private async Task<(DecisionConnection Connection, string? Key)?> Read(Guid spaceId, Guid userId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(cancellationToken);
        var connection = await scope.ServiceProvider.GetRequiredService<DecisionConnectionRepository>().FindConnection(cancellationToken);

        if (connection is not { Status: ConnectionStatus.Ready })
        {
            return null;
        }

        var key = connection.KeySecretId is { } secret
            ? await scope.ServiceProvider.GetRequiredService<ISecretVault>().Reveal(secret, cancellationToken)
            : null;
        await work.Commit(cancellationToken);
        return (connection, key);
    }

    #endregion
}
