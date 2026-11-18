namespace Drover.Native.Pages;

/// <summary>
/// Which bot is spending, to decide what to change: a table, cost first, the crew as the hierarchy it is, the
/// biggest spender first at every level. No chart and no totals card. Each bot is a row with its figures; on a phone
/// each bot becomes a block with its figures labeled. With no table semantics to lean on, every figure carries its
/// column's name for assistive technology, as the web's header cells give it.
/// </summary>
public sealed class UsageTable : Grid
{
    #region Private Fields

    // The columns share the width as the web's table does: the bot's column gets the room, a figure is never broken.
    private static readonly double[] shares = [28, 22, 14, 14, 22];

    private readonly StackPanel column = new();

    private readonly IReadOnlyList<UsageTableRow> rows;

    private bool? narrow;

    #endregion

    #region Constructors

    public UsageTable(string caption, IReadOnlyList<UsageTableRow> rows)
    {
        this.rows = rows;
        AutomationProperties.SetName(column, caption);
        Children.Add(column);
        SizeChanged += (_, _) => Fit();
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Private Methods

    // A phone is narrower than the first breakpoint. The layout changes only when the outcome differs from last time.
    private void Fit()
    {
        var phone = ActualWidth > 0 && ActualWidth < (double)Styles.Dictionary["DvBreakPhone"];
        if (phone != narrow)
        {
            narrow = phone;
            Show();
        }
    }

    private void Show()
    {
        column.Children.Clear();
        if (narrow is true)
        {
            foreach (var row in rows)
            {
                column.Children.Add(Block(row));
            }

            return;
        }

        column.Children.Add(Header());
        foreach (var row in rows)
        {
            column.Children.Add(Row(row));
        }
    }

    #endregion

    #region Wide

    private static Grid Columns()
    {
        var grid = new Grid();
        foreach (var share in shares)
        {
            grid.ColumnDefinitions.Add(new() { Width = new(share, GridUnitType.Star) });
        }

        return grid;
    }

    // The column names are drawn once; each figure says its own column's name to assistive technology.
    private Border Header()
    {
        var grid = Columns();
        for (var index = 0; index < UsagePageViewModel.Columns.Count; index++)
        {
            var heading = Words(UsagePageViewModel.Columns[index], "DvTextLabel", noWrap: true);
            heading.Foreground = Tokens.Brush(this, "DvText2");
            AutomationProperties.SetAccessibilityView(heading, AccessibilityView.Raw);
            Place(grid, index, heading);
        }

        return Ruled(grid);
    }

    private Border Row(UsageTableRow row)
    {
        var grid = Columns();
        Place(grid, 0, BotCell(row));
        Place(grid, 1, CostLines(row));
        Place(grid, 2, FigureLines(2, row.TokensInOwn, row.TokensInBeneath, row.TokensInOwnWords, row.TokensInBeneathWords));
        Place(grid, 3, FigureLines(3, row.TokensOutOwn, row.TokensOutBeneath, row.TokensOutOwnWords, row.TokensOutBeneathWords));
        Place(grid, 4, Mono(row.Model, Named(4, row.Model)));
        return Ruled(grid);
    }

    private static void Place(Grid grid, int index, FrameworkElement cell)
    {
        cell.Margin = Tokens.Space("DvSpace3", "DvSpace2");
        SetColumn(cell, index);
        grid.Children.Add(cell);
    }

    #endregion

    #region Narrow

    // One block for each bot, each figure with its column's name before it.
    private Border Block(UsageTableRow row)
    {
        var block = new StackPanel { Padding = new(0, Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2")) };
        block.Children.Add(Inset(BotCell(row)));
        block.Children.Add(Labeled(1, CostLines(row)));
        block.Children.Add(Labeled(2, FigureLines(2, row.TokensInOwnWords, row.TokensInBeneathWords, row.TokensInOwnWords, row.TokensInBeneathWords)));
        block.Children.Add(Labeled(3, FigureLines(3, row.TokensOutOwnWords, row.TokensOutBeneathWords, row.TokensOutOwnWords, row.TokensOutBeneathWords)));
        block.Children.Add(Labeled(4, Mono(row.Model, Named(4, row.Model))));
        return Ruled(block);
    }

    private static FrameworkElement Inset(FrameworkElement cell)
    {
        cell.Margin = Tokens.Space("DvSpace3", "DvSpace1");
        return cell;
    }

    // The label is drawn for the eye; the figures already say it to assistive technology.
    private static StackPanel Labeled(int index, FrameworkElement figures)
    {
        var line = new StackPanel { Margin = Tokens.Space("DvSpace3", "DvSpace1") };
        var label = Words($"{UsagePageViewModel.Columns[index]}:", "DvTextCaption");
        AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
        line.Children.Add(label);
        line.Children.Add(figures);
        return line;
    }

    #endregion

    #region Cells

    private StackPanel BotCell(UsageTableRow row)
    {
        var cell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Number("DvSpace2"),
            Padding = new(row.Depth * Tokens.Number("DvSpace4"), 0, 0, 0),
        };
        var avatar = new Avatar { BotName = row.BotName, Hue = row.Hue, Size = 24, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(avatar, AccessibilityView.Raw);

        // The bot whose runs are listed below is marked by weight and by being said to be current, not by color alone.
        var name = new Link
        {
            Text = row.BotName,
            Address = row.Address,
            Ink = "DvText",
            HoverInk = "DvText",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[row.Chosen ? "DvWeightHeading" : "DvWeightLabel"],
        };
        AutomationProperties.SetName(name, row.Label);
        AutomationProperties.SetItemStatus(name, row.Chosen ? "Current" : string.Empty);

        cell.Children.Add(avatar);
        cell.Children.Add(name);
        if (row.Removed)
        {
            // A removed bot's figures still count, so it stays in the crew and says it is gone.
            var gone = Words("removed", "DvTextCaption");
            gone.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAccessibilityView(gone, AccessibilityView.Raw);
            cell.Children.Add(gone);
        }

        return cell;
    }

    private static StackPanel CostLines(UsageTableRow row)
    {
        var lines = new StackPanel();
        lines.Children.Add(Figure(Words(row.CostOwn, "DvTextBody", noWrap: true), row.CostOwn));
        if (row.CostBeneath is { } beneath)
        {
            lines.Children.Add(Figure(Words(beneath, "DvTextSecondary"), beneath));
        }

        if (row.LeftOut is { } leftOut)
        {
            lines.Children.Add(Figure(Words(leftOut, "DvTextSecondary"), leftOut));
        }

        return lines;

        static TextBlock Figure(TextBlock words, string said)
        {
            AutomationProperties.SetName(words, Named(1, said));
            return words;
        }
    }

    // The figure on its own, and the figure with the bots beneath it, each read out with its column and what it counts.
    private StackPanel FigureLines(int index, string own, string? beneath, string ownWords, string? beneathWords)
    {
        var lines = new StackPanel();
        lines.Children.Add(Mono(own, Named(index, ownWords)));
        if (beneath is not null)
        {
            var second = Mono(beneath, Named(index, beneathWords ?? beneath));
            second.Foreground = Tokens.Brush(this, "DvText2");
            lines.Children.Add(second);
        }

        return lines;
    }

    private TextBlock Mono(string text, string name)
    {
        var mono = new TextBlock
        {
            Text = text,
            Style = (Style)Styles.Dictionary["DvTextCode"],
            FontSize = Tokens.Number("DvText12"),
            LineHeight = (double)Styles.Dictionary["DvLine12"],
            Foreground = Tokens.Brush(this, "DvText"),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(mono, name);
        return mono;
    }

    private static string Named(int index, string words) => $"{UsagePageViewModel.Columns[index]}: {words}";

    private static TextBlock Words(string text, string style, bool noWrap = false)
    {
        var words = Styles.Text(text, style);
        words.FontSize = Tokens.Number("DvText13");
        words.LineHeight = (double)Styles.Dictionary["DvLine13"];
        words.TextWrapping = noWrap ? TextWrapping.NoWrap : TextWrapping.Wrap;
        return words;
    }

    // A row's line below it, as the table draws one under each row.
    private Border Ruled(UIElement content) => new()
    {
        Child = content,
        BorderBrush = Tokens.Brush(this, "DvLine"),
        BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder")),
    };

    #endregion
}
