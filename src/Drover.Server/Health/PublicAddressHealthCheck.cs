namespace Drover.Server.Health;

/// <summary>Whether the operator has said at which address people open this server. Optional until something needs it.</summary>
public sealed class PublicAddressHealthCheck(IConfiguration configuration) : IHealthCheck
{
    #region Interface Implementations

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(PublicAddress.Read(configuration) is { } address
            ? HealthCheckResult.Healthy($"People open Drover at {address.GetLeftPart(UriPartial.Authority)}.")
            : StatusChecks.Off($"Not set. Signing in to a tool server needs it: set {PublicAddress.Variable} to the address people open Drover at."));

    #endregion
}
