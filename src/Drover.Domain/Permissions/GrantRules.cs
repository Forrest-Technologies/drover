namespace Drover.Domain.Permissions;

/// <summary>
/// The grant rule from docs/architecture/04-tenancy-and-permissions.md, as pure functions. The SQL function
/// <c>effective_grants</c> implements the same rule; both are tested against one table of cases.
/// </summary>
public static class GrantRules
{
    #region Public Methods

    /// <summary>
    /// The effective grant for one tool: the minimum of the bot's own grant and every ancestor's. Pass one entry
    /// per bot on the path, the bot first, with <c>null</c> for a bot that has no grant. Any <c>null</c>, or an
    /// empty path, means no grant, which means the tool is invisible. A bot whose own closure row is missing is
    /// not in the tree, so <paramref name="botIsInTree"/> false gives no grant whatever the grants say.
    /// </summary>
    public static GrantEffect? Effective(IEnumerable<GrantEntry?> path, bool botIsInTree = true)
    {
        if (!botIsInTree)
        {
            return null;
        }

        GrantEffect? effective = null;

        foreach (var entry in path)
        {
            if (entry is not { } grant)
            {
                return null;
            }

            var counted = Counted(grant);
            effective = effective is { } soFar ? Min(soFar, counted) : counted;
        }

        return effective;
    }

    /// <summary>A grant whose tool definition no human has confirmed counts as ask, unless it already denies.</summary>
    public static GrantEffect Counted(GrantEntry grant) =>
        grant.ReviewState is ReviewState.NeedsReview ? Min(grant.Effect, GrantEffect.Ask) : grant.Effect;

    public static GrantEffect Min(GrantEffect first, GrantEffect second) => first < second ? first : second;

    #endregion
}
