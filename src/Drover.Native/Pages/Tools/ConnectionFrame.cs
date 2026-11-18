namespace Drover.Native.Pages;

/// <summary>
/// One thing of a list on a faceted frame, with room inside and one space between what is put in it: a model
/// connection, a tool server, the form that adds one. A frame that holds a form has the stronger edge and more room
/// between its parts.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Body))]
public sealed class ConnectionFrame : Grid
{
    #region Private Fields

    private readonly Facet frame = new() { Cut = Tokens.Number("DvCut") };

    private readonly StackPanel body = new();

    #endregion

    #region Constructors

    public ConnectionFrame()
    {
        frame.Padding = new(Tokens.Number("DvSpace4"));
        frame.Children.Add(body);
        Children.Add(frame);
        Paint();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty StrongProperty = Dp.Register<ConnectionFrame, bool>(nameof(Strong), false, frame => frame.Paint());

    /// <summary>What is put in the frame, stacked.</summary>
    public UIElementCollection Body => body.Children;

    /// <summary>A frame that holds a form: the edge is the stronger line, and its parts have more room.</summary>
    public bool Strong
    {
        get => (bool)GetValue(StrongProperty);
        set => SetValue(StrongProperty, value);
    }

    #endregion

    #region Private Methods

    private void Paint()
    {
        frame.Fill = Tokens.Brush(this, "DvBg1");
        frame.Edge = Tokens.Brush(this, Strong ? "DvText3" : "DvLine");
        body.Spacing = Tokens.Number(Strong ? "DvSpace4" : "DvSpace3");
    }

    #endregion
}
