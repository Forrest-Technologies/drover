namespace Drover.Native.Controls;

/// <summary>
/// A pressable control that is one choice of several, of which one is taken: a radio button to assistive
/// technology, which is told whether this is the one taken and can take it. It stays invokable, as a button is.
/// </summary>
internal sealed class ChoicePeer(Microsoft.UI.Xaml.Controls.Button owner, bool taken)
    : ButtonAutomationPeer(owner), ISelectionItemProvider
{
    #region Properties

    public bool IsSelected => taken;

    /// <summary>The group is named by the region around the choices, which is not a selection container.</summary>
    public IRawElementProviderSimple? SelectionContainer => null;

    #endregion

    #region Interface Implementations

    public void Select() => ((IInvokeProvider)this).Invoke();

    /// <summary>One choice is taken at a time, so adding this one takes it.</summary>
    public void AddToSelection() => Select();

    /// <summary>One choice is always taken, so this one is let go only by taking another.</summary>
    public void RemoveFromSelection()
    {
    }

    #endregion

    #region Private Methods

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.RadioButton;

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);

    #endregion
}
