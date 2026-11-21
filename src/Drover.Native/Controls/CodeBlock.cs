namespace Drover.Native.Controls;

/// <summary>
/// A block of code or machine output, drawn by what it holds (<see cref="CodeContent"/>, drawn by
/// <see cref="CodeDrawing"/>). The header names the kind, and holds the wrap toggle, the button that opens the block
/// full screen, and the copy icon. Lines never wrap unless the wrap toggle says so: a long line scrolls sideways
/// inside the block and never widens the page. Past the cap only the start is laid out, with a button for the rest.
/// The body is one tab stop named by the kind, so it scrolls by keyboard too.
/// </summary>
public sealed class CodeBlock : Grid
{
    #region Private Fields

    // Wrapping is chosen once for the session: a block made after the choice starts from it.
    private static bool wrapsInSession;

    private readonly Border box = new();

    private readonly Border bar = new();

    private readonly TextBlock label = Styles.Text(null, "DvTextMono");

    private readonly IconButton wrap = new() { Icon = IconName.Wrap, Label = CodeContent.WrapLabel, Ink = "DvText2", Pressed = false };

    private readonly IconButton expand = new() { Icon = IconName.Expand, Label = CodeViewerModel.OpenLabel, Ink = "DvText2" };

    private readonly CopyButton copy = new() { Label = OutputWords.CopyCode, Ink = "DvText2", VerticalAlignment = VerticalAlignment.Center };

    private readonly ScrollViewer body;

    private readonly StackPanel under = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };

    private readonly Button showAll = new() { Kind = ButtonKind.Quiet };

    private readonly Button hex = new() { Kind = ButtonKind.Quiet };

    private readonly Button literal = new() { Kind = ButtonKind.Quiet, Content = CodeContent.ShowLiteral };

    private CodeContent? content;

    private bool wraps = wrapsInSession;

    private bool all;

    private CodeBytesView bytesView = CodeBytesView.Summary;

    #endregion

    #region Constructors

    public CodeBlock()
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        copy.Text = () => Shown.Source;
        wrap.Click += (_, _) => Wrap();
        expand.Click += (_, _) => CodeViewer.Open(this, Shown, wraps, CopyLabel, expand);
        showAll.Click += (_, _) => ShowAll();
        hex.Click += (_, _) => Show(bytesView is CodeBytesView.Hex ? CodeBytesView.Summary : CodeBytesView.Hex);
        literal.Click += (_, _) => Show(bytesView is CodeBytesView.Literal ? CodeBytesView.Summary : CodeBytesView.Literal);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(wrap);
        actions.Children.Add(expand);
        actions.Children.Add(copy);

        var top = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetColumn(actions, 1);
        top.Children.Add(label);
        top.Children.Add(actions);
        bar.Child = top;
        bar.Padding = new(Tokens.Number("DvSpace3"), 0, 0, 0);
        bar.BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder"));

        body = new()
        {
            Padding = new(Tokens.Number("DvSpace3")),
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            FocusVisualPrimaryThickness = new(Tokens.Number("DvFocusWidth")),
            FocusVisualSecondaryThickness = new(0),
            FocusVisualMargin = new(Tokens.Number("DvFocusWidth")),
        };

        under.Padding = new(Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2"), Tokens.Number("DvSpace1"));
        under.Children.Add(hex);
        under.Children.Add(literal);
        under.Children.Add(showAll);

        var column = new Grid();
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        SetRow(body, 1);
        SetRow(under, 2);
        column.Children.Add(bar);
        column.Children.Add(body);
        column.Children.Add(under);
        box.Child = column;
        box.BorderThickness = new(Tokens.Number("DvBorder"));
        box.CornerRadius = new(Tokens.Number("DvRadiusCard"));
        Children.Add(box);
        this.WhenShownOrRethemed(Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty CodeProperty =
        Dp.Register<CodeBlock, string>(nameof(Code), string.Empty, block => block.Read());

    public static readonly DependencyProperty CodeLanguageProperty =
        Dp.Register<CodeBlock, string?>(nameof(CodeLanguage), null, block => block.Read());

    public static readonly DependencyProperty BoundedProperty = Dp.Register<CodeBlock, bool>(nameof(Bounded), false, block => block.Fit(block.wraps));

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>
    /// The language the code is written in, or a word that names what the block is ("Output"). A language it knows
    /// decides how the block is drawn; any other word is the label of plain text. Named so because every element
    /// already has a <c>Language</c>.
    /// </summary>
    public string? CodeLanguage
    {
        get => (string?)GetValue(CodeLanguageProperty);
        set => SetValue(CodeLanguageProperty, value);
    }

    /// <summary>The label of plain text given no language: "Text", or "Code" for a fence.</summary>
    public string PlainLabel { get; set; } = CodeContent.PlainLabel;

    /// <summary>The copy icon's name: "Copy code", or "Copy output" for what a tool printed or returned.</summary>
    public string CopyLabel
    {
        get => copy.Label;
        set => copy.Label = value;
    }

    /// <summary>
    /// Shows at most <c>DvOutputLines</c> lines of height and scrolls down past them: for arguments and results in a
    /// card, so a long block never pushes the conversation away.
    /// </summary>
    public bool Bounded
    {
        get => (bool)GetValue(BoundedProperty);
        set => SetValue(BoundedProperty, value);
    }

    // What the block holds, read once for its text and language.
    private CodeContent Shown => content ??= CodeContent.Detect(Code, CodeLanguage, PlainLabel);

    #endregion

    #region Private Methods

    private void Read()
    {
        content = null;
        all = false;
        bytesView = CodeBytesView.Summary;

        if (IsLoaded)
        {
            Draw();
        }
    }

    private void Wrap()
    {
        wraps = wrapsInSession = !wraps;
        Draw();
    }

    private void ShowAll()
    {
        all = true;
        Draw();
        body.Focus(FocusState.Keyboard);
    }

    private void Show(CodeBytesView view)
    {
        bytesView = view;
        Draw();
    }

    private void Draw()
    {
        box.Background = Tokens.Brush(this, "DvBg2");
        box.BorderBrush = Tokens.Brush(this, "DvLine");
        bar.BorderBrush = Tokens.Brush(this, "DvLine");
        label.Foreground = Tokens.Brush(this, "DvText2");
        body.FocusVisualPrimaryBrush = Tokens.Brush(this, "DvEmber");

        var read = Shown;
        label.Text = read.Label;
        AutomationProperties.SetName(body, read.Label);

        // A table with too many columns, cells, or characters for a grid is drawn as text, capped until all of it is
        // asked for. All of it is held to the same bounds, so asking for it never builds a grid past them.
        var shown = read is { Kind: CodeKind.Table, Table: { } wide } && CodeLines.TableLayoutOf(wide, all) is TableLayout.Text
            ? read with { Kind = CodeKind.Plain, Family = CodeFamily.None }
            : read;

        var isText = shown.Kind is not (CodeKind.Table or CodeKind.Binary or CodeKind.Bytes) || bytesView is CodeBytesView.Literal;
        wrap.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        wrap.Pressed = wraps;
        Fit(isText && wraps);

        hex.Visibility = shown.Kind is CodeKind.Binary or CodeKind.Bytes ? Visibility.Visible : Visibility.Collapsed;
        hex.Content = bytesView is CodeBytesView.Hex ? CodeContent.HideHex : CodeContent.ShowHex;
        literal.Visibility = shown.Kind is CodeKind.Bytes ? Visibility.Visible : Visibility.Collapsed;

        var (drawn, cutTo) = new CodeDrawing(this).Body(shown, wraps, all, bytesView);
        body.Content = drawn;
        showAll.Visibility = cutTo is null ? Visibility.Collapsed : Visibility.Visible;
        showAll.Content = cutTo ?? string.Empty;
        under.Visibility = under.Children.Any(part => part.Visibility is Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
    }

    // Wrapped lines scroll down only; unwrapped lines scroll sideways. A bounded block scrolls down past its height.
    private void Fit(bool wrapped)
    {
        body.HorizontalScrollMode = wrapped ? ScrollMode.Disabled : ScrollMode.Auto;
        body.HorizontalScrollBarVisibility = wrapped ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        body.VerticalScrollMode = Bounded ? ScrollMode.Auto : ScrollMode.Disabled;
        body.VerticalScrollBarVisibility = Bounded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        body.MaxHeight = Bounded ? Tokens.Number("DvOutputLines") * (double)Styles.Dictionary["DvLine13"] : double.PositiveInfinity;
    }

    #endregion
}
