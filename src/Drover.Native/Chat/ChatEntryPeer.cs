namespace Drover.Native.Chat;

/// <summary>
/// A message of a conversation to assistive technology: a group. An older reply that keeps its actions out of sight
/// is also something that opens and closes: it says whether its actions are shown, and can be asked to show or hide
/// them, as Enter, Space or a press does.
/// </summary>
internal sealed class ChatEntryPeer(ChatEntry owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider
{
    #region Properties

    public ExpandCollapseState ExpandCollapseState =>
        owner.ActionsShown ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    #endregion

    #region Public Methods

    public void Expand() => owner.ShowActions(true);

    public void Collapse() => owner.ShowActions(false);

    /// <summary>Says that the reply's actions came into view or went, given what they were before.</summary>
    public void Changed(bool shownBefore) => RaisePropertyChangedEvent(
        ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
        shownBefore ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
        ExpandCollapseState);

    #endregion

    #region Private Methods

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.ExpandCollapse && owner.Reveals
            ? this
            : base.GetPatternCore(patternInterface);

    #endregion
}
