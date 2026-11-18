namespace Drover.Native.Chat;

/// <summary>
/// A line of words that can be given the focus but is not a stop on the way through the page: the line that takes
/// the place of a button once the button has done its work, so the focus has somewhere to be and the line is read
/// out. It leaves the tab order again when the focus moves on.
/// </summary>
internal sealed class FocusLine : ContentControl
{
    #region Private Fields

    private readonly TextBlock words = Styles.Text(null, "DvTextSecondary");

    #endregion

    #region Constructors

    public FocusLine()
    {
        Content = words;
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        UseSystemFocusVisuals = true;
        FocusVisualPrimaryThickness = new(Tokens.Number("DvFocusWidth"));
        FocusVisualSecondaryThickness = new(0);
        FocusVisualMargin = new(-(Tokens.Number("DvFocusOffset") + Tokens.Number("DvFocusWidth")));
        LostFocus += (_, _) => IsTabStop = false;
        this.WhenShownOrRethemed(() => FocusVisualPrimaryBrush = Tokens.Brush(this, "DvEmber"));
    }

    #endregion

    #region Properties

    /// <summary>The line's own text block, for a line that holds a link among its words.</summary>
    public TextBlock Words => words;

    #endregion

    #region Public Methods

    /// <summary>Says the line as one sentence, which is also its name for a screen reader.</summary>
    public void Say(string sentence)
    {
        words.Text = sentence;
        AutomationProperties.SetName(this, sentence);
    }

    public void TakeFocus()
    {
        IsTabStop = true;
        Focus(FocusState.Programmatic);
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Text);

    #endregion
}
