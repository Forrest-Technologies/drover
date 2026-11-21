namespace Drover.Native.Controls;

/// <summary>
/// Draws what a block of code or output holds (<see cref="CodeContent"/>), the same way in a block and in its full
/// screen view: highlighted code in text colors and weights only, diff and log lines marked in their status colors with
/// their signs and level words kept, a table as a grid, and bytes as a summary or a hex view. Colors are read from
/// <paramref name="scope"/>'s theme.
/// </summary>
internal sealed class CodeDrawing(FrameworkElement scope)
{
    #region Public Methods

    /// <summary>
    /// The body of a block, and the words of the button that shows the rest when only the start is laid out. With
    /// <paramref name="all"/> nothing is cut.
    /// </summary>
    public (FrameworkElement Drawn, string? CutTo) Body(CodeContent shown, bool wraps, bool all, CodeBytesView view) => shown.Kind switch
    {
        CodeKind.Table => Table(shown.Table!, all),
        CodeKind.Binary or CodeKind.Bytes when view is CodeBytesView.Hex => Hex(shown.Bytes, all),
        CodeKind.Bytes when view is CodeBytesView.Literal => Text(shown, wraps, all),
        CodeKind.Binary or CodeKind.Bytes => (Summary(shown), null),
        _ => Text(shown, wraps, all),
    };

    /// <summary>
    /// A whole table as two grids with the same column widths: its header row, to stay in view, and its rows, to scroll
    /// under it. The header is null for a table without one.
    /// </summary>
    public (Grid? Header, Grid Rows) SplitTable(CodeTable table)
    {
        var header = table.Header is null ? null : Grid(table, 1);
        var rows = Grid(table, table.Rows.Count);
        AutomationProperties.SetName(rows, table.Label);
        var widths = new double[table.ColumnCount];

        if (header is not null)
        {
            for (var column = 0; column < table.ColumnCount; column++)
            {
                var cell = Cell(table.Header![column], table.Numeric[column], "DvWeightLabel");
                widths[column] = Math.Max(widths[column], Measured(cell));
                Place(header, cell, 0, column);
            }

            UnderlineHeader(header);
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            for (var column = 0; column < table.ColumnCount; column++)
            {
                var cell = ValueCell(table, row, column);
                widths[column] = Math.Max(widths[column], Measured(cell));
                Place(rows, cell, row, column);
            }
        }

        foreach (var grid in new[] { header, rows }.OfType<Grid>())
        {
            for (var column = 0; column < table.ColumnCount; column++)
            {
                grid.ColumnDefinitions[column].Width = new(Math.Ceiling(widths[column]));
            }
        }

        return (header, rows);
    }

    #endregion

    #region Text

    // Text kinds as one block of runs: highlighted code, marked diff and log lines, a quiet prompt before a command.
    private (FrameworkElement Drawn, string? CutTo) Text(CodeContent shown, bool wraps, bool all)
    {
        var capped = all ? new CappedText(shown.Text, CodeLines.LineCount(shown.Text), false) : CodeLines.Capped(shown.Text);
        var text = CodeLines.ExpandTabs(capped.Shown);
        var block = Styles.Text(null, "DvTextCode");
        block.IsTextSelectionEnabled = true;
        block.TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
        block.Foreground = Tokens.Brush(scope, "DvText");

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

    private static void AddLines(TextBlock block, string text, Func<string, IEnumerable<Microsoft.UI.Xaml.Documents.Run>> runs)
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
        var run = new Microsoft.UI.Xaml.Documents.Run { Text = text, Foreground = Tokens.Brush(scope, ink) };

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
    private (FrameworkElement Drawn, string? CutTo) Table(CodeTable table, bool all)
    {
        var cut = !all && CodeLines.TableLayoutOf(table) is TableLayout.FirstRows;
        var count = cut ? CodeLines.MaxLines : table.Rows.Count;
        var offset = table.Header is null ? 0 : 1;
        var grid = Grid(table, count + offset);
        AutomationProperties.SetName(grid, table.Label);

        if (table.Header is { } header)
        {
            for (var column = 0; column < table.ColumnCount; column++)
            {
                Place(grid, Cell(header[column], table.Numeric[column], "DvWeightLabel"), 0, column);
            }

            UnderlineHeader(grid);
        }

        for (var row = 0; row < count; row++)
        {
            for (var column = 0; column < table.ColumnCount; column++)
            {
                Place(grid, ValueCell(table, row, column), row + offset, column);
            }
        }

        return (grid, cut ? CodeLines.ShowAllRows(table.Rows.Count) : null);
    }

    private Grid Grid(CodeTable table, int rows)
    {
        var grid = new Grid { ColumnSpacing = Tokens.Number("DvSpace4") };

        for (var column = 0; column < table.ColumnCount; column++)
        {
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        }

        for (var row = 0; row < rows; row++)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }

        return grid;
    }

    private void Place(Grid grid, TextBlock cell, int row, int column)
    {
        Microsoft.UI.Xaml.Controls.Grid.SetRow(cell, row);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    // The header row stands over a line, so the names read apart from the values.
    private void UnderlineHeader(Grid grid)
    {
        var line = new Border { BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder")), BorderBrush = Tokens.Brush(scope, "DvLine") };
        Microsoft.UI.Xaml.Controls.Grid.SetColumnSpan(line, Math.Max(1, grid.ColumnDefinitions.Count));
        grid.Children.Insert(0, line);
    }

    private TextBlock ValueCell(CodeTable table, int row, int column)
    {
        var value = table.Rows[row][column];
        var cell = Cell(value, table.Numeric[column], null);
        AutomationProperties.SetName(cell, table.Header is { } names ? $"{names[column]}: {value}" : value);
        return cell;
    }

    private TextBlock Cell(string value, bool numeric, string? weight)
    {
        var cell = Styles.Text(value, "DvTextCode");
        cell.Foreground = Tokens.Brush(scope, "DvText");
        cell.IsTextSelectionEnabled = true;
        cell.HorizontalAlignment = numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        cell.TextAlignment = numeric ? TextAlignment.Right : TextAlignment.Left;

        if (weight is not null)
        {
            cell.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[weight];
            cell.Padding = new(0, 0, 0, Tokens.Number("DvSpace1"));
        }

        return cell;
    }

    private static double Measured(TextBlock cell)
    {
        cell.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return cell.DesiredSize.Width;
    }

    #endregion

    #region Bytes

    // Bytes are never shown as escapes: their size says what they are, and the hex view is one press away.
    private TextBlock Summary(CodeContent shown)
    {
        var words = Styles.Text(shown.Label, "DvTextCaption");
        words.Foreground = Tokens.Brush(scope, "DvText2");
        return words;
    }

    // Offset, hex, and printable characters, a line for every 16 bytes; past the cap, the first lines.
    private (FrameworkElement Drawn, string? CutTo) Hex(IReadOnlyList<byte> bytes, bool all)
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
}

/// <summary>How a block of bytes is shown: by its size, as hex, or as the literal was written.</summary>
internal enum CodeBytesView
{
    Summary,
    Hex,
    Literal,
}
