namespace Drover.Native.Controls;

/// <summary>One action of a <see cref="Menu"/>. Choosing it closes the menu, puts the focus back on the menu's button, and then acts.</summary>
public sealed class MenuItem : PlainButton
{
    #region Constructors

    public MenuItem()
    {
        RingInside = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0);
    }

    #endregion

    #region Properties

    /// <summary>Raised when the item is chosen, after the menu has closed.</summary>
    public event EventHandler? Selected;

    #endregion

    #region Private Methods

    internal void RaiseSelected() => Selected?.Invoke(this, EventArgs.Empty);

    protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.MenuItem);

    #endregion
}
