namespace Drover.Native.Controls;

/// <summary>
/// A block of code or machine output, drawn by what it holds (<see cref="CodeContent"/>): code highlighted for its
/// language, a shell command after a quiet prompt mark, JSON laid out, a table as a table, a diff and a log with their
/// lines marked, and bytes as a summary with a hex view on demand. The header names the kind, and holds the wrap
/// toggle and the copy icon. Lines never wrap unless the wrap toggle says so: a long line scrolls sideways inside the
/// block and never widens the page. Past the cap only the start is laid out, with a button for the rest. The body is
/// one tab stop named by the kind, so it scrolls by keyboard too.
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

    private readonly CopyButton copy = new() { Label = OutputWords.CopyCode, Ink = "DvText2", VerticalAlignment = VerticalAlignment.Center };

    private readonly ScrollViewer body;

    private readonly StackPanel under = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };

    private readonly Button showAll = new() { Kind = ButtonKind.Quiet };

    private readonly Button hex = new() { Kind = ButtonKind.Quiet };

    private readonly Button literal = new() { Kind = ButtonKind.Quiet, Content = CodeContent.ShowLiteral };

    private CodeContent? content;

    private bool wraps = wrapsInSession;

    private bool all;

    private BytesView bytesView = BytesView.Summary;

    #endregion

    #region Constructors

    public CodeBlock()
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        copy.Text = () => content?.Source ?? Code;
        wrap.Click += (_, _) => Wrap();
        showAll.Click += (_, _) => ShowAll();
        hex.Click += (_, _) => Show(bytesView is BytesView.Hex ? BytesView.Summary : BytesView.Hex);
        literal.Click += (_, _) => Show(bytesView is BytesView.Literal ? BytesView.Summary : BytesView.Literal);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(wrap);
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

    /// <summary>What the block holds, once read.</summary>
    public CodeContent? Content => content;

    #endregion

    #region Private Methods

    private void Read()
    {
        content = CodeContent.Detect(Code, CodeLanguage, PlainLabel);
        all = false;
        bytesView = BytesView.Summary;

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

    private void Show(BytesView view)
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

        var read = content ??= CodeContent.Detect(Code, CodeLanguage, PlainLabel);
        label.Text = read.Label;
        AutomationProperties.SetName(body, read.Label);

        // A table too wide to lay out as a grid at once is drawn as capped text until all of it is asked for.
        var shown = !all && read is { Kind: CodeKind.Table, Table: { } wide } && CodeLines.TableLayoutOf(wide) is TableLayout.Text
            ? read with { Kind = CodeKind.Plain, Family = CodeFamily.None }
            : read;

        var isText = shown.Kind is not (CodeKind.Table or CodeKind.Binary or CodeKind.Bytes) || bytesView is BytesView.Literal;
        wrap.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        wrap.Pressed = wraps;
        Fit(isText && wraps);

        hex.Visibility = shown.Kind is CodeKind.Binary or CodeKind.Bytes ? Visibility.Visible : Visibility.Collapsed;
        hex.Content = bytesView is BytesView.Hex ? CodeContent.HideHex : CodeContent.ShowHex;
        literal.Visibility = shown.Kind is CodeKind.Bytes ? Visibility.Visible : Visibility.Collapsed;

        var (drawn, cutTo) = shown.Kind switch
        {
            CodeKind.Table => DrawTable(shown.Table!),
            CodeKind.Binary or CodeKind.Bytes when bytesView is BytesView.Hex => DrawHex(shown.Bytes),
            CodeKind.Bytes when bytesView is BytesView.Literal => DrawText(shown),
            CodeKind.Binary or CodeKind.Bytes => (Summary(shown), null),
            _ => DrawText(shown),
        };

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

    #region Text

    // Text kinds as one block of runs: highlighted code, marked diff and log lines, a quiet prompt before a command.
    private (FrameworkElement Drawn, string? CutTo) DrawText(CodeContent shown)
    {
        var capped = all ? new CappedText(shown.Text, CodeLines.LineCount(shown.Text), false) : CodeLines.Capped(shown.Text);
        var text = CodeLines.ExpandTabs(capped.Shown);
        var block = Styles.Text(null, "DvTextCode");
        block.IsTextSelectionEnabled = true;
        block.TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
        block.Foreground = Tokens.Brush(this, "DvText");

        if (shown.Kind is CodeKind.Shell)
        {
            // The prompt is decoration: the command after it is what is read and copied.
            block.Inlines.Add(Run(CodeContent.PromptMark, "DvText3"));
        }

        switch (shown.Kind)
        {
            case CodeKind.Diff:
                AddLines(block, text, DiffRuns);
                break;
            case CodeKind.Log:
                AddLines(block, text, LogRuns);
                break;
            default:
                AddTokens(block, text, CodeHighlighter.Tokens(text, shown.Family));
                break;
        }

        return (block, capped.IsCut ? CodeLines.ShowAll(capped.TotalLines) : null);
    }

    private void AddTokens(TextBlock block, string text, IReadOnlyList<CodeToken> tokens)
    {
        var at = 0;

        foreach (var token in tokens)
        {
            if (token.Start > at)
            {
                block.Inlines.Add(Run(text[at..token.Start]));
            }

            block.Inlines.Add(TokenRun(text.Substring(token.Start, token.Length), token.Kind));
            at = token.Start + token.Length;
        }

        if (at < text.Length)
        {
            block.Inlines.Add(Run(text[at..]));
        }
    }

    // Syntax keeps to text colors and weights; the accents keep their one meaning each.
    private Microsoft.UI.Xaml.Documents.Run TokenRun(string text, CodeTokenKind kind) => kind switch
    {
        CodeTokenKind.Keyword => Run(text, "DvText", "DvWeightHeading"),
        CodeTokenKind.Key => Run(text, "DvText", "DvWeightLabel"),
        CodeTokenKind.Comment => Run(text, "DvText3"),
        CodeTokenKind.Quoted or CodeTokenKind.Punctuation => Run(text, "DvText2"),
        _ => Run(text, "DvText"),
    };

    private void AddLines(TextBlock block, string text, Func<string, IEnumerable<Microsoft.UI.Xaml.Documents.Run>> runs)
    {
        var lines = CodeLines.Split(text);

        for (var index = 0; index < lines.Count; index++)
        {
            foreach (var run in runs(lines[index]))
            {
                block.Inlines.Add(run);
            }

            if (index < lines.Count - 1)
            {
                block.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
            }
        }
    }

    // Each line keeps its own + or -, so the color is never the only sign of what changed.
    private IEnumerable<Microsoft.UI.Xaml.Documents.Run> DiffRuns(string line) => CodeLines.DiffKindOf(line) switch
    {
        DiffLineKind.Added => [Run(line, "DvOk")],
        DiffLineKind.Removed => [Run(line, "DvDanger")],
        DiffLineKind.Hunk => [Run(line, "DvText3")],
        DiffLineKind.Header => [Run(line, "DvText2", "DvWeightLabel")],
        _ => [Run(line)],
    };

    // A level word keeps its word and takes its status color; a time is a caption.
    private IEnumerable<Microsoft.UI.Xaml.Documents.Run> LogRuns(string line)
    {
        var read = CodeLines.LogLineOf(line);
        List<(Range Span, string Ink, string? Weight)> marks = [];

        if (read.Time is { } time)
        {
            marks.Add((time, "DvText3", null));
        }

        if (read.Level is { } level)
        {
            marks.Add((level, read.Severity switch
            {
                LogSeverity.Failure => "DvDanger",
                LogSeverity.Attention => "DvWarn",
                _ => "DvText2",
            }, "DvWeightLabel"));
        }

        var at = 0;
        foreach (var (span, ink, weight) in marks.OrderBy(mark => mark.Span.Start.Value))
        {
            var (start, end) = (span.Start.Value, span.End.Value);
            if (start > at)
            {
                yield return Run(line[at..start]);
            }

            yield return Run(line[start..end], ink, weight);
            at = end;
        }

        if (at < line.Length)
        {
            yield return Run(line[at..]);
        }
    }

    private Microsoft.UI.Xaml.Documents.Run Run(string text, string ink = "DvText", string? weight = null)
    {
        var run = new Microsoft.UI.Xaml.Documents.Run { Text = text, Foreground = Tokens.Brush(this, ink) };

        if (weight is not null)
        {
            run.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[weight];
        }

        return run;
    }

    #endregion

    #region Table

    // A real table: the header row in label weight over a line, numeric columns aligned to the end, monospaced cells.
    // Each value is named with its column for someone who cannot see the grid.
    private (FrameworkElement Drawn, string? CutTo) DrawTable(CodeTable table)
    {
        var cut = !all && CodeLines.TableLayoutOf(table) is TableLayout.FirstRows;
        var rows = table.Rows.Take(cut ? CodeLines.MaxLines : table.Rows.Count);
        var grid = new Grid { ColumnSpacing = Tokens.Number("DvSpace4") };
        AutomationProperties.SetName(grid, table.Label);

        for (var column = 0; column < table.ColumnCount; column++)
        {
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        }

        var row = 0;
        if (table.Header is { } header)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var line = new Border { BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder")), BorderBrush = Tokens.Brush(this, "DvLine") };
            SetColumnSpan(line, table.ColumnCount);
            grid.Children.Add(line);

            for (var column = 0; column < table.ColumnCount; column++)
            {
                var cell = Cell(header[column], table.Numeric[column], "DvWeightLabel");
                cell.Margin = new(0, 0, 0, Tokens.Number("DvSpace1"));
                SetColumn(cell, column);
                grid.Children.Add(cell);
            }

            row++;
        }

        foreach (var values in rows)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });

            for (var column = 0; column < table.ColumnCount; column++)
            {
                var cell = Cell(values[column], table.Numeric[column], null);
                AutomationProperties.SetName(cell, table.Header is { } names ? $"{names[column]}: {values[column]}" : values[column]);
                SetRow(cell, row);
                SetColumn(cell, column);
                grid.Children.Add(cell);
            }

            row++;
        }

        return (grid, cut ? CodeLines.ShowAllRows(table.Rows.Count) : null);
    }

    private TextBlock Cell(string value, bool numeric, string? weight)
    {
        var cell = Styles.Text(value, "DvTextCode");
        cell.Foreground = Tokens.Brush(this, "DvText");
        cell.IsTextSelectionEnabled = true;
        cell.HorizontalAlignment = numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        cell.TextAlignment = numeric ? TextAlignment.Right : TextAlignment.Left;

        if (weight is not null)
        {
            cell.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[weight];
        }

        return cell;
    }

    #endregion

    #region Bytes

    // Bytes are never shown as escapes: their size says what they are, and the hex view is one press away.
    private TextBlock Summary(CodeContent shown)
    {
        var words = Styles.Text(shown.Label, "DvTextCaption");
        words.Foreground = Tokens.Brush(this, "DvText2");
        return words;
    }

    // Offset, hex, and printable characters, a line for every 16 bytes; past the cap, the first lines.
    private (FrameworkElement Drawn, string? CutTo) DrawHex(IReadOnlyList<byte> bytes)
    {
        var lines = CodeBytes.HexLines(bytes);
        var cut = !all && lines.Count > CodeLines.MaxLines;
        var block = Styles.Text(null, "DvTextCode");
        block.IsTextSelectionEnabled = true;

        foreach (var line in cut ? lines.Take(CodeLines.MaxLines) : lines)
        {
            if (block.Inlines.Count > 0)
            {
                block.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
            }

            block.Inlines.Add(Run($"{line.Offset}  ", "DvText3"));
            block.Inlines.Add(Run(line.Hex.PadRight((CodeBytes.BytesPerLine * 3) + 1)));
            block.Inlines.Add(Run($"  {line.Printable}", "DvText2"));
        }

        return (block, cut ? CodeLines.ShowAll(lines.Count) : null);
    }

    #endregion

    #region Nested Types

    private enum BytesView
    {
        Summary,
        Hex,
        Literal,
    }

    #endregion
}
