namespace Drover.Native.Layouts;

/// <summary>
/// Sign-in and setup: one narrow column on the yard, the wordmark above a faceted panel, one thing to do. What is
/// put in it is stacked in the panel.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Body))]
public sealed class EntryLayout : Grid
{
    #region Private Fields

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace5") };

    private readonly Facet panel = new() { Cut = Tokens.Number("DvSpace4") };

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly TextBlock word = Styles.Text("Drover", "DvTextH3");

    #endregion

    #region Constructors

    public EntryLayout()
    {
        word.CharacterSpacing = (int)Tokens.Dictionary["DvTracking"];
        word.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(word, AutomationHeadingLevel.None);
        var wordmark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        wordmark.Children.Add(new DroverMark { VerticalAlignment = VerticalAlignment.Center });
        wordmark.Children.Add(word);

        panel.Children.Add(body);
        column.Children.Add(wordmark);
        column.Children.Add(panel);
        column.MaxWidth = Tokens.Number("DvEntryWidth");
        column.HorizontalAlignment = HorizontalAlignment.Stretch;

        Children.Add(new Yard());
        Children.Add(new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsTabStop = false });
        SizeChanged += (_, _) => Fit();
        Fit();
    }

    #endregion

    #region Properties

    /// <summary>What the screen holds, stacked in the panel.</summary>
    public UIElementCollection Body => body.Children;

    #endregion

    #region Private Methods

    // A wider window gives the column more room above and the panel more room inside.
    private void Fit()
    {
        var wide = ActualWidth >= (double)Styles.Dictionary["DvBreakEntry"];
        var side = Tokens.Number("DvSpace4");

        column.Padding = new(side, Tokens.Number(wide ? "DvSpace7" : "DvSpace5"), side, Tokens.Number("DvSpace7"));
        panel.Padding = wide ? Tokens.Space("DvSpace5", "DvSpace6") : Tokens.Space("DvSpace4", "DvSpace5");
    }

    #endregion
}
