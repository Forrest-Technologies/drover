namespace Drover.Native.Pages;

/// <summary>The small pieces the extensions screens are put together from: text in the sizes their style sheets use.</summary>
internal static class Parts
{
    #region Public Methods

    /// <summary>A line of small text: 13 in the secondary color, such as a hint or a fact's description.</summary>
    public static TextBlock Small(string? text) => Inked(Styles.Text(text, "DvTextLabel"), "DvText2", body: true);

    /// <summary>Text the machine wrote, in the machine typeface at a size from the scale, in a color token.</summary>
    public static TextBlock Machine(string? text, string size, string ink)
    {
        var block = Styles.Text(text, "DvTextMono");
        var points = Tokens.Number(size);
        block.FontSize = points;
        block.LineHeight = points * Tokens.Number("DvLeadingText");
        return Inked(block, ink, body: false);
    }

    /// <summary>A heading in one of the heading styles, at a level the screen's outline needs.</summary>
    public static TextBlock Heading(string? text, string style, AutomationHeadingLevel level)
    {
        var block = Styles.Text(text, style);
        AutomationProperties.SetHeadingLevel(block, level);
        return block;
    }

    /// <summary>A small title in the label weight and the main color: the name of a block, at a level of the outline or none.</summary>
    public static TextBlock Title(string? text, AutomationHeadingLevel level)
    {
        var block = Inked(Styles.Text(text, "DvTextLabel"), "DvText", body: false);
        AutomationProperties.SetHeadingLevel(block, level);
        return block;
    }

    /// <summary>The name of a fact in the label weight, then what it says, one under the other, as a definition list does.</summary>
    public static StackPanel Fact(string term, UIElement description)
    {
        var fact = new StackPanel();
        fact.Children.Add(Inked(Styles.Text(term, "DvTextLabel"), "DvText", body: false));
        fact.Children.Add(description);
        return fact;
    }

    /// <summary>Plain text in the body size and the main color, which wraps anywhere a word cannot.</summary>
    public static TextBlock Body(string? text) => Styles.Text(text);

    #endregion

    #region Private Methods

    // A color from a token, kept in step with the theme the text is shown in.
    private static TextBlock Inked(TextBlock block, string ink, bool body)
    {
        if (body)
        {
            block.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightBody"];
        }

        block.Loaded += (_, _) => block.Foreground = Tokens.Brush(block, ink);
        block.ActualThemeChanged += (_, _) => block.Foreground = Tokens.Brush(block, ink);
        return block;
    }

    #endregion
}
