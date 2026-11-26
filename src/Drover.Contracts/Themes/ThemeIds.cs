namespace Drover.Contracts.Themes;

/// <summary>The ids Drover keeps for its own themes. A theme from anywhere else may not take one, so an id always names one look.</summary>
public static class ThemeIds
{
    #region Private Fields

    public static readonly IReadOnlyList<string> Reserved =
    [
        "front-office", "night-yard", "signal-room", "paper-ledger", "workbench", "blueprint", "quiet-hours", "foundry",
    ];

    #endregion
}
