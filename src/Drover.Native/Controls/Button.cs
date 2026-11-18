namespace Drover.Native.Controls;

/// <summary>
/// A button on a faceted frame. Quiet by default; primary is the one thing a person came to do, in the color of
/// their intent; danger is for what cannot be taken back. A button names its action: "Connect calendar".
/// </summary>
public sealed class Button : Pressable
{
    #region Constructors

    public Button()
        : base("DvFacetTemplate")
    {
        Padding = new(Tokens.Number("DvSpace4"), 0, Tokens.Number("DvSpace4"), 0);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty KindProperty =
        Dp.Register<Button, ButtonKind>(nameof(Kind), ButtonKind.Quiet, button => button.Repaint());

    public ButtonKind Kind
    {
        get => (ButtonKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    #endregion

    #region Private Methods

    protected override void Paint()
    {
        var (fill, edge, ink) = Colors();
        var frame = (Facet)Frame!;

        frame.Cut = Tokens.Number("DvCutS");
        frame.Fill = Tokens.Brush(this, fill);
        frame.Edge = Tokens.Brush(this, edge);
        frame.Ring = HasKeyboardFocus ? Tokens.Brush(this, Kind is ButtonKind.Primary ? "DvOnAccent" : "DvEmber") : null;
        Foreground = Tokens.Brush(this, ink);
        FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[Kind is ButtonKind.Primary && IsEnabled ? "DvWeightHeading" : "DvWeightLabel"];
    }

    // Fill, edge, and text for the kind and the state. Nothing is lit at rest: the primary action takes its color
    // only when it can be taken.
    private (string Fill, string Edge, string Ink) Colors() => (Kind, IsEnabled, IsHovered) switch
    {
        (ButtonKind.Primary, false, _) => ("DvBg2", "DvLine", "DvText3"),
        (ButtonKind.Primary, true, true) => ("DvEmber", "DvText", "DvOnAccent"),
        (ButtonKind.Primary, true, false) => ("DvEmber", "DvEmber", "DvOnAccent"),
        (_, false, _) => ("DvBg2", "DvLine", "DvText3"),
        (ButtonKind.Danger, true, true) => ("DvBg3", "DvText2", "DvDanger"),
        (ButtonKind.Danger, true, false) => ("DvBg2", "DvDanger", "DvDanger"),
        (_, true, true) => ("DvBg3", "DvText2", "DvText"),
        _ => ("DvBg2", "DvText3", "DvText"),
    };

    #endregion
}
