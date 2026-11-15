namespace Drover.Domain.Policy;

/// <summary>
/// The budget of one run tree in one space: the deployment's ceilings, lowered by the space's own settings. A
/// delegation that would take the tree past any of them is refused with the limit's name and value.
/// </summary>
public sealed record DelegationLimits(int MaxDepth, int MaxRuns, int MaxCostCents)
{
    #region Properties

    /// <summary>The cost budget in the price list's currency.</summary>
    public decimal MaxCost => MaxCostCents / 100m;

    #endregion
}
