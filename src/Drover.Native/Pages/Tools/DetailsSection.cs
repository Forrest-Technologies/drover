namespace Drover.Native.Pages;

/// <summary>
/// A part of a frame that opens and closes under a line, with a name that is its button: the tools of a tool server,
/// the less common choices of the form that adds one. What it holds is in the screen only while it is open.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Body))]
public sealed class DetailsSection : Grid
{
    #region Private Fields

    private readonly RuleBox rule = new() { Space = string.Empty };

    private readonly PlainButton toggle = new() { RingInside = true, Radius = null, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };

    private readonly TextBlock title = Styles.Text(null);

    private readonly ContentPresenter content = new();

    #endregion

    #region Constructors

    public DetailsSection()
    {
        title.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        toggle.Content = title;
        toggle.MinHeight = Tokens.Number("DvTargetTouch");
        toggle.Click += (_, _) => Open = !Open;

        var column = new StackPanel();
        column.Children.Add(toggle);
        column.Children.Add(content);
        rule.Child = column;
        Children.Add(rule);
        Show();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TitleProperty = Dp.Register<DetailsSection, string>(nameof(Title), string.Empty, section => section.Show());

    public static readonly DependencyProperty OpenProperty = Dp.Register<DetailsSection, bool>(nameof(Open), false, section => section.Show());

    public static readonly DependencyProperty BodyProperty = Dp.Register<DetailsSection, object?>(nameof(Body), null, section => section.Show());

    /// <summary>What the part holds, in a word or two. It is the name of the button that opens it.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Whether the part is open. It can be bound both ways.</summary>
    public bool Open
    {
        get => (bool)GetValue(OpenProperty);
        set => SetValue(OpenProperty, value);
    }

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        title.Text = Title;
        AutomationProperties.SetName(toggle, Title);
        AutomationProperties.SetItemStatus(toggle, Open ? "Expanded" : "Collapsed");
        content.Content = Open ? Body : null;
    }

    #endregion
}
