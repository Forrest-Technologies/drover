namespace Drover.Native.Controls;

/// <summary>
/// A block of code or output in the machine typeface, with the copy icon that copies all of it. The text can be selected, and
/// a long line scrolls sideways inside the block and never widens the page.
/// </summary>
public sealed class CodeBlock : Grid
{
    #region Private Fields

    private readonly Border box = new();

    private readonly Border bar = new();

    private readonly TextBlock language = Styles.Text(null, "DvTextMono");

    private readonly CopyButton copy = new() { Label = OutputWords.CopyCode, VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock code = Styles.Text(null, "DvTextCode");

    private readonly ScrollViewer body;

    #endregion

    #region Constructors

    public CodeBlock()
    {
        code.IsTextSelectionEnabled = true;
        language.VerticalAlignment = VerticalAlignment.Center;
        copy.Text = () => Code;

        var top = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetColumn(copy, 1);
        top.Children.Add(language);
        top.Children.Add(copy);
        bar.Child = top;
        bar.Padding = new(Tokens.Number("DvSpace3"), Tokens.Number("DvSpace1"), Tokens.Number("DvSpace1"), Tokens.Number("DvSpace1"));
        bar.BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder"));

        body = new()
        {
            Content = code,
            Padding = new(Tokens.Number("DvSpace3")),
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsTabStop = true,
        };
        AutomationProperties.SetName(body, "Code");

        var column = new Grid();
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        SetRow(body, 1);
        column.Children.Add(bar);
        column.Children.Add(body);
        box.Child = column;
        box.BorderThickness = new(Tokens.Number("DvBorder"));
        box.CornerRadius = new(Tokens.Number("DvRadiusCard"));
        Children.Add(box);
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty CodeProperty =
        Dp.Register<CodeBlock, string>(nameof(Code), string.Empty, block => block.code.Text = block.Code);

    public static readonly DependencyProperty CodeLanguageProperty = Dp.Register<CodeBlock, string?>(
        nameof(CodeLanguage), null, block => block.language.Text = string.IsNullOrWhiteSpace(block.CodeLanguage) ? string.Empty : block.CodeLanguage);

    public static readonly DependencyProperty WrapsProperty = Dp.Register<CodeBlock, bool>(nameof(Wraps), false, block => block.Fit());

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>The language the code is written in, shown in the bar. Named so because every element already has a <c>Language</c>.</summary>
    public string? CodeLanguage
    {
        get => (string?)GetValue(CodeLanguageProperty);
        set => SetValue(CodeLanguageProperty, value);
    }

    /// <summary>The copy icon's name: "Copy code", or "Copy output" for what a tool printed or returned.</summary>
    public string CopyLabel
    {
        get => copy.Label;
        set => copy.Label = value;
    }

    /// <summary>
    /// Wraps long lines and scrolls down past a set number of them, in place of scrolling sideways: for arguments and
    /// results in a card, so a phone shows all of a line.
    /// </summary>
    public bool Wraps
    {
        get => (bool)GetValue(WrapsProperty);
        set => SetValue(WrapsProperty, value);
    }

    #endregion

    #region Private Methods

    private void Fit()
    {
        code.TextWrapping = Wraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
        body.HorizontalScrollMode = Wraps ? ScrollMode.Disabled : ScrollMode.Auto;
        body.HorizontalScrollBarVisibility = Wraps ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        body.VerticalScrollMode = Wraps ? ScrollMode.Auto : ScrollMode.Disabled;
        body.VerticalScrollBarVisibility = Wraps ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        body.MaxHeight = Wraps ? Tokens.Number("DvOutputLines") * Tokens.Number("DvLine14") : double.PositiveInfinity;
    }

    private void Paint()
    {
        box.Background = Tokens.Brush(this, "DvBg2");
        box.BorderBrush = Tokens.Brush(this, "DvLine");
        bar.BorderBrush = Tokens.Brush(this, "DvLine");
        language.Foreground = Tokens.Brush(this, "DvText2");
    }

    #endregion
}
