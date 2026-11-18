namespace Drover.Native.Pages;

/// <summary>
/// One box to tick, with its words beside it: a bot to share a sandbox with, a tool a rule names, a day. It is the
/// platform's check box, so it is a tab stop, turned with Space, and read out with its state; its colors are tokens,
/// its mark the color of a person's intent.
/// </summary>
public sealed class CheckItem : CheckBox
{
    #region Private Fields

    private bool quiet;

    #endregion

    #region Constructors

    public CheckItem(string label, bool machine = false)
    {
        var words = machine ? BotParts.Mono(label).Inked("DvText") : Styles.Text(label);
        words.VerticalAlignment = VerticalAlignment.Center;
        Content = words;
        MinHeight = Tokens.Number("DvTargetTouch");
        VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, label);
        Checked += (_, _) => Raise();
        Unchecked += (_, _) => Raise();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    /// <summary>Raised when a person ticks or clears the box, with whether it is ticked.</summary>
    public event Action<bool>? Changed;

    #endregion

    #region Public Methods

    /// <summary>Shows whether the box is ticked, without saying a person changed it.</summary>
    public void Show(bool ticked)
    {
        quiet = true;
        IsChecked = ticked;
        quiet = false;
    }

    #endregion

    #region Private Methods

    private void Raise()
    {
        if (!quiet)
        {
            Changed?.Invoke(IsChecked is true);
        }
    }

    private void Paint()
    {
        var ember = Tokens.Brush(this, "DvEmber");
        foreach (var state in new[] { string.Empty, "PointerOver", "Pressed" })
        {
            Resources[$"CheckBoxCheckBackgroundFillChecked{state}"] = ember;
            Resources[$"CheckBoxCheckBackgroundStrokeChecked{state}"] = ember;
            Resources[$"CheckBoxCheckGlyphForegroundChecked{state}"] = Tokens.Brush(this, "DvOnAccent");
            Resources[$"CheckBoxCheckBackgroundFillUnchecked{state}"] = Tokens.Brush(this, "DvBg0");
            Resources[$"CheckBoxCheckBackgroundStrokeUnchecked{state}"] = Tokens.Brush(this, "DvText3");
            Resources[$"CheckBoxForegroundUnchecked{state}"] = Tokens.Brush(this, "DvText");
            Resources[$"CheckBoxForegroundChecked{state}"] = Tokens.Brush(this, "DvText");
            Resources[$"CheckBoxBackgroundUnchecked{state}"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            Resources[$"CheckBoxBackgroundChecked{state}"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        Foreground = Tokens.Brush(this, "DvText");
        FocusVisualPrimaryBrush = ember;
        FocusVisualPrimaryThickness = new(Tokens.Number("DvFocusWidth"));
        FocusVisualSecondaryThickness = new(0);
    }

    #endregion
}
