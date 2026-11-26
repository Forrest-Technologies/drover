namespace Drover.Server.Models;

/// <summary>
/// Builds the provider catalog when the server starts, so a registration mistake, such as two providers that claim
/// one kind, stops the server with a sentence that names the kind instead of surfacing on the first request that
/// needs a provider. A kind the operator's list names but no provider serves is logged, because it offers nothing.
/// </summary>
public sealed class ModelProviderStartupCheck(
    ModelProviderCatalog catalog,
    IOptions<DeploymentPolicyOptions> options,
    ILogger<ModelProviderStartupCheck> logger) : IHostedService
{
    #region Interface Implementations

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var rules = options.Value.Rules;

        foreach (var kind in rules.AllowedModelProviderKinds.Where(kind => catalog.Find(kind) is null))
        {
            logger.LogWarning(
                "{Rule} names {Kind}, but this server has no provider of that kind, so it offers nothing",
                nameof(DeploymentPolicy.AllowedModelProviderKinds),
                kind);
        }

        logger.LogInformation(
            "This server offers {Count} kinds of model provider: {Kinds}",
            catalog.Offered.Count,
            string.Join(", ", catalog.Offered.Select(descriptor => descriptor.Kind)));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion
}
