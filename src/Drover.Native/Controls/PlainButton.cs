namespace Drover.Native.Controls;

/// <summary>
/// A pressable control on a plain surface, the base of the icon button, the menu's items, the tabs, and the links
/// of the bar. Its colors are token names, set by the control that uses it. Its focus ring is the ember ring, two
/// wide: outside the control with a gap of two, or inside it where something would clip a ring outside.
/// </summary>
public class PlainButton : Pressable
{
    #region Constructors

    public PlainButton()
        : base("DvPlainTemplate")
    {
    }

    #endregion

    #region Properties

    /// <summary>The surface at rest, as a token name. Nothing when null.</summary>
    public string? Fill { get; set; }

    public string? HoverFill { get; set; } = "DvBg3";

    /// <summary>The one pixel line around the surface, as a token name. None when null.</summary>
    public string? Line { get; set; }

    public string Ink { get; set; } = "DvText";

    public string HoverInk { get; set; } = "DvText";

    public string DisabledInk { get; set; } = "DvText3";

    /// <summary>The corner radius, as a token name.</summary>
    public string Radius { get; set; } = "DvRadiusControl";

    /// <summary>A line two wide under the control, as a token name: the mark of the showing tab. None when null.</summary>
    public string? Underline { get; set; }

    /// <summary>Draws the focus ring inside the control, for a control inside a scrolling or clipped box.</summary>
    public bool RingInside { get; set; }

    /// <summary>The color the text and any icon inside take in the current state.</summary>
    public Brush CurrentInk => Tokens.Brush(this, !IsEnabled ? DisabledInk : IsHovered ? HoverInk : Ink);

    #endregion

    #region Private Methods

    protected override void Paint()
    {
        var frame = (Border)Frame!;
        var fill = IsEnabled && (IsHovered || (RingInside && HasKeyboardFocus)) ? HoverFill ?? Fill : Fill;
        var focus = Tokens.Number("DvFocusWidth");
        var offset = RingInside ? 0 : Tokens.Number("DvFocusOffset") + focus;

        frame.Background = fill is null ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : Tokens.Brush(this, fill);
        frame.BorderBrush = Line is null ? null : Tokens.Brush(this, Line);
        frame.BorderThickness = new(Line is null ? 0 : Tokens.Number("DvBorder"));
        frame.CornerRadius = new(Tokens.Number(Radius));

        if (Line is null)
        {
            // The space of the underline is always kept, so the words do not move when the line appears.
            frame.BorderBrush = Underline is null ? null : Tokens.Brush(this, Underline);
            frame.BorderThickness = new(0, 0, 0, Underline is null ? 0 : focus);
        }

        Foreground = CurrentInk;

        // The platform draws the ring only when the keyboard moved the focus, which is the rule.
        UseSystemFocusVisuals = true;
        FocusVisualPrimaryBrush = Tokens.Brush(this, "DvEmber");
        FocusVisualPrimaryThickness = new(focus);
        FocusVisualSecondaryThickness = new(0);
        FocusVisualMargin = new(-offset);
        Painted();
    }

    /// <summary>For a control with parts of its own to recolor, such as an icon.</summary>
    protected virtual void Painted()
    {
    }

    #endregion
}
