namespace Drover.Engine.Policy;

/// <summary>
/// The one place that computes an effective rule. The deployment policy comes first and a space can only narrow
/// it: where the deployment forbids something, no space setting turns it back on. Feature code asks this service a
/// specific question and never asks which preset is selected.
/// </summary>
public sealed class PolicyService(IOptions<DeploymentPolicyOptions> options, AccountRepository accounts)
{
    #region Properties

    public string Preset => options.Value.Preset;

    public DeploymentPolicy Deployment => options.Value.Rules;

    public string? TermsVersion => options.Value.TermsVersion;

    #endregion

    #region Public Methods

    /// <summary>The rules for a space, read from its settings. Runs in the tenant scope the caller opened.</summary>
    public async Task<EffectivePolicy> GetEffective(Guid spaceId, CancellationToken cancellationToken)
    {
        var space = await accounts.GetSpace(spaceId, cancellationToken);
        return Narrow(space?.Settings ?? new SpaceSettings());
    }

    public EffectivePolicy Narrow(SpaceSettings space) =>
        new(
            AllowPrivateAddresses: Deployment.AllowPrivateAddresses && space.AllowPrivateAddresses != false,
            TaintedRunEscalation: !(Deployment.SpaceMayDisableTaintedRunEscalation && space.TaintedRunEscalation == false),
            AutoLoadRemoteImages: Deployment.SpaceMayAutoLoadRemoteImages && space.AutoLoadRemoteImages == true);

    #endregion
}
