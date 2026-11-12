namespace Drover.Server.Policy;

public static class PolicyEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapPolicyEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/status", GetStatus)
            .RequireAuthorization()
            .WithName("GetStatus")
            .WithSummary("The active deployment preset and every rule with its setting.");
        return api;
    }

    #endregion

    #region Endpoints

    private static Ok<PolicyStatus> GetStatus(PolicyService policy) =>
        TypedResults.Ok(new PolicyStatus(
            policy.Preset,
            [
                .. DeploymentPolicy.Rules.Select(rule => new PolicyRuleStatus(
                    rule.Name,
                    rule.ReadText(policy.Deployment),
                    rule.Enforcement.IsEnforced,
                    rule.Enforcement.Where)),
            ]));

    #endregion
}
