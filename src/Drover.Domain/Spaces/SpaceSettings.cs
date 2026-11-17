namespace Drover.Domain.Spaces;

/// <summary>
/// Limits an owner can set on a space. A null limit means the deployment policy decides. A space can only narrow
/// what the deployment allows: <c>PolicyService</c> computes the effective value.
/// </summary>
public sealed record SpaceSettings
{
    #region Properties

    public int? MaxBotDepth { get; init; }

    public int? MaxChildrenPerBot { get; init; }

    public int? MaxBots { get; init; }

    /// <summary>Lowers the deployment's <c>MaxDelegationDepth</c> for this space. It can never raise it.</summary>
    public int? MaxDelegationDepth { get; init; }

    /// <summary>Lowers the deployment's <c>MaxRunsPerDelegationTree</c> for this space. It can never raise it.</summary>
    public int? MaxRunsPerDelegationTree { get; init; }

    /// <summary>Lowers the deployment's <c>MaxDelegationTreeCostCents</c> for this space. It can never raise it.</summary>
    public int? MaxDelegationTreeCostCents { get; init; }

    /// <summary>False forbids requests to private addresses from this space. True, or null, changes nothing.</summary>
    public bool? AllowPrivateAddresses { get; init; }

    /// <summary>False turns tainted-run escalation off where the deployment lets a space. Null leaves it on.</summary>
    public bool? TaintedRunEscalation { get; init; }

    /// <summary>True turns remote images on where the deployment lets a space. Null leaves them off.</summary>
    public bool? AutoLoadRemoteImages { get; init; }

    #endregion
}
