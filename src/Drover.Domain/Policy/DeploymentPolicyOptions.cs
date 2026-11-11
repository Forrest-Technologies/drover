namespace Drover.Domain.Policy;

/// <summary>
/// What the operator sets: the preset by name, any rules tightened beyond it, and the version of the terms of
/// service that sign-up asks people to accept. <see cref="Rules"/> starts as the chosen preset.
/// </summary>
public sealed class DeploymentPolicyOptions
{
    public string Preset { get; set; } = DeploymentPolicy.SelfHostedName;

    public string? TermsVersion { get; set; }

    public DeploymentPolicy Rules { get; set; } = DeploymentPolicy.SelfHosted;
}
