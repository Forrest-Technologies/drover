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

    /// <summary>Lowers the deployment's <c>MaxStepsPerRun</c> for this space. It can never raise it.</summary>
    public int? MaxStepsPerRun { get; init; }

    /// <summary>Lowers the deployment's <c>MaxTokensPerRun</c> for this space. It can never raise it.</summary>
    public int? MaxTokensPerRun { get; init; }

    /// <summary>False forbids requests to private addresses from this space. True, or null, changes nothing.</summary>
    public bool? AllowPrivateAddresses { get; init; }

    /// <summary>True turns remote images on where the deployment lets a space. Null leaves them off.</summary>
    public bool? AutoLoadRemoteImages { get; init; }

    /// <summary>Ordered models used when a bot has no fallback list of its own.</summary>
    public IReadOnlyList<ModelFallback> ModelFallbacks { get; init; } = [];

    /// <summary>Which models listen, speak and hold live calls for this space. Null lets the server choose each.</summary>
    public VoiceSettings? Voice { get; init; }

    #endregion
}
