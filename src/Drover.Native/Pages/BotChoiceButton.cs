namespace Drover.Native.Pages;

/// <summary>
/// One bot to choose, on a faceted frame as wide as the list: the bot's head and its name. The frame lights a step
/// while the pointer is over it, and the ring shows when the keyboard is on it.
/// </summary>
public sealed class BotChoiceButton : Pressable
{
    #region Constructors

    public BotChoiceButton()
        : base("DvFacetTemplate")
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Padding = Tokens.Space("DvSpace3", "DvSpace2");
        MinHeight = Tokens.Number("DvSpace7");
    }

    #endregion

    #region Private Methods

    protected override void Paint()
    {
        var frame = (Facet)Frame!;
        frame.Role = FacetRole.Frame;
        frame.Fill = Tokens.Brush(this, IsHovered ? "DvBg2" : "DvBg1");
        frame.Edge = Tokens.Brush(this, "DvLine");
        frame.Ring = HasKeyboardFocus ? Tokens.Brush(this, "DvEmber") : null;
        Foreground = Tokens.Brush(this, "DvText");
    }

    #endregion
}
