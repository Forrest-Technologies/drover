namespace Drover.Native.Pages;

/// <summary>The heading of a part of the editor's form: a line above it, and its words at the size of a small heading.</summary>
internal sealed class PartHeading : Border
{
    #region Private Fields

    private readonly TextBlock words = Parts.Heading(null, "DvTextH3", AutomationHeadingLevel.Level2);

    #endregion

    #region Constructors

    public PartHeading()
    {
        Child = words;
        Padding = new(0, Tokens.Number("DvSpace3"), 0, 0);
        BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        this.WhenShownOrRethemed(() => BorderBrush = Tokens.Brush(this, "DvLine"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TextProperty =
        Dp.Register<PartHeading, string>(nameof(Text), string.Empty, heading => heading.words.Text = heading.Text);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    #endregion
}
