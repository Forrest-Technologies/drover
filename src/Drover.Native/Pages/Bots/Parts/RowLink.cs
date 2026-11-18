namespace Drover.Native.Pages;

/// <summary>
/// The name of a row that opens something: a link to assistive technology, in the row's own text color, whose focus
/// ring is drawn by the row around itself rather than around the name.
/// </summary>
public sealed class RowLink : PlainButton
{
    #region Private Fields

    private bool hadKeyboardFocus;

    #endregion

    #region Constructors

    public RowLink()
    {
        (Ink, HoverInk, HoverFill, Radius) = ("DvText", "DvText", null, null);
        Padding = new(0);
        MinHeight = Targets.IsCoarse ? Tokens.Number("DvTargetTouch") : 0;
        HorizontalAlignment = HorizontalAlignment.Left;
        HorizontalContentAlignment = HorizontalAlignment.Left;
    }

    #endregion

    #region Properties

    /// <summary>Raised when the keyboard moves the focus here or away, so the row can draw its ring.</summary>
    public event Action<bool>? KeyboardFocus;

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.Hyperlink);

    protected override void Paint()
    {
        base.Paint();
        UseSystemFocusVisuals = false;

        if (hadKeyboardFocus != HasKeyboardFocus)
        {
            hadKeyboardFocus = HasKeyboardFocus;
            KeyboardFocus?.Invoke(HasKeyboardFocus);
        }
    }

    #endregion
}
