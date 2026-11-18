namespace Drover.Native.Pages;

/// <summary>
/// Which bot is spending, to decide what to change: a table, cost first, the crew as the hierarchy it is, the
/// biggest spender first at every level. No chart and no totals card. Each bot is a row with its figures; on a
/// phone each bot becomes a block with its figures labeled. A figure says in its name what it counts, as the web's
/// columns do for a screen reader.
/// </summary>
public sealed class UsageTable : Grid
{
    #region Private Fields

    // The columns share the width as a table's do: the bot's column gets the room, a figure is never broken.
    private static readonly double[] shares = [28, 22, 14, 14, 22];

    private readonly StackPanel column = new();

    private bool? narrow;

    #endregion

    #region Constructors

    public UsageTable()
    {
        Children.Add(column);
        SizeChanged += (_, _) => Fit();
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TableProperty = Dp.Register<UsageTable, UsageTableModel?>(nameof(Table), null, table => table.Show());

    /// <summary>The table to show: its caption, its rows, and whether anything ran.</summary>
    public UsageTableModel? Table
    {
        get => (UsageTableModel?)GetValue(TableProperty);
        set => SetValue(TableProperty, value);
    }

    /// <summary>Raised when a bot's name is chosen: its most expensive runs are what the person wants next.</summary>
    public event Action<Guid>? BotChosen;

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
        if (Table is not { } table)
        {
            return;
        }

        AutomationProperties.SetName(column, table.Caption);

        if (table.IsEmpty)
        {
            column.Children.Add(Styles.Text(UsageTableModel.EmptyNote, "DvTextSecondary"));
            return;
        }

        if (narrow is true)
        {
            ShowBlocks(table);
        }
        else
        {
            column.Children.Add(Header());
            foreach (var row in table.Rows)
            {
                column.Children.Add(Row(row));
            }
        }
    }

    #endregion

    #region Wide

    private Grid Columns()
    {
        var grid = new Grid();
        foreach (var share in shares)
        {
            grid.ColumnDefinitions.Add(new() { Width = new(share, GridUnitType.Star) });
        }

        return grid;
    }

    private Border Header()
    {
        var grid = Columns();
        for (var index = 0; index < UsageTableModel.Columns.Count; index++)
        {
            var heading = Words(UsageTableModel.Columns[index], "DvTextLabel");
            heading.TextWrapping = TextWrapping.NoWrap;
            heading.Margin = Cell();
            Grid.SetColumn(heading, index);
            grid.Children.Add(heading);
        }

        return Ruled(grid);
    }

    private Border Row(UsageTableRow row)
    {
        var grid = Columns();
        Place(grid, 0, BotCell(row));
        Place(grid, 1, CostLines(row));
        Place(grid, 2, FigureLines(row.TokensInOwn, row.TokensInBeneath, row.TokensInOwnWords, row.TokensInBeneathWords));
        Place(grid, 3, FigureLines(row.TokensOutOwn, row.TokensOutBeneath, row.TokensOutOwnWords, row.TokensOutBeneathWords));
        Place(grid, 4, Mono(row.Model, row.Model));
        return Ruled(grid);
    }

    private void Place(Grid grid, int index, FrameworkElement cell)
    {
        cell.Margin = Cell();
        Grid.SetColumn(cell, index);
        grid.Children.Add(cell);
    }

    private Thickness Cell() => Tokens.Space("DvSpace3", "DvSpace2");

    #endregion

    #region Narrow

    // One block for each bot, each figure with its column's name before it.
    private void ShowBlocks(UsageTableModel table)
    {
        foreach (var row in table.Rows)
        {
            var block = new StackPanel { Spacing = Tokens.Number("DvSpace1"), Padding = new(0, Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2")) };
            block.Children.Add(Indented(BotCell(row)));
            block.Children.Add(Labeled(UsageTableModel.Columns[1], CostLines(row)));
            block.Children.Add(Labeled(UsageTableModel.Columns[2], FigureLines(row.TokensInOwnWords, row.TokensInBeneathWords, row.TokensInOwnWords, row.TokensInBeneathWords)));
            block.Children.Add(Labeled(UsageTableModel.Columns[3], FigureLines(row.TokensOutOwnWords, row.TokensOutBeneathWords, row.TokensOutOwnWords, row.TokensOutBeneathWords)));
            block.Children.Add(Labeled(UsageTableModel.Columns[4], Mono(row.Model, row.Model)));
            column.Children.Add(Ruled(block));
        }
    }

    private FrameworkElement Indented(FrameworkElement cell)
    {
        cell.Margin = Tokens.Space("DvSpace3", "DvSpace1");
        return cell;
    }

    private StackPanel Labeled(string label, FrameworkElement figures)
    {
        var line = new StackPanel { Margin = Tokens.Space("DvSpace3", "DvSpace1") };
        var name = Words($"{label}:", "DvTextCaption");
        line.Children.Add(name);
        line.Children.Add(figures);
        return line;
    }

    #endregion

    #region Cells

    private StackPanel BotCell(UsageTableRow row)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2"), Margin = new(row.Depth * Tokens.Number("DvSpace4"), 0, 0, 0) };
        var avatar = new Avatar { BotName = row.BotName, Hue = row.Hue, Size = 24, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(avatar, AccessibilityView.Raw);

        var name = new TextBlock
        {
            Text = row.BotName,
            TextDecorations = Windows.UI.Text.TextDecorations.Underline,
            TextWrapping = TextWrapping.Wrap,
        };
        var choose = new PlainButton
        {
            Content = name,
            Padding = new(0),
            MinHeight = 0,
            HoverFill = null,
            Radius = null,
            Ink = "DvText",
            HoverInk = "DvText",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[row.Chosen ? "DvWeightHeading" : "DvWeightLabel"],
        };
        AutomationProperties.SetName(choose, row.Label);
        AutomationProperties.SetItemStatus(choose, row.Chosen ? "Current" : string.Empty);
        choose.Click += (_, _) => BotChosen?.Invoke(row.BotId);

        cell.Children.Add(avatar);
        cell.Children.Add(choose);
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

    private StackPanel CostLines(UsageTableRow row)
    {
        var lines = new StackPanel();
        lines.Children.Add(Words(row.CostOwn, "DvTextBody", noWrap: true));
        if (row.CostBeneath is { } beneath)
        {
            lines.Children.Add(Words(beneath, "DvTextSecondary"));
        }

        if (row.LeftOut is { } leftOut)
        {
            lines.Children.Add(Words(leftOut, "DvTextSecondary"));
        }

        return lines;
    }

    // The figure on its own, and the figure with the bots beneath it. The words are read out beside the table's columns.
    private StackPanel FigureLines(string own, string? beneath, string ownWords, string? beneathWords)
    {
        var lines = new StackPanel();
        lines.Children.Add(Mono(own, ownWords));
        if (beneath is not null)
        {
            var second = Mono(beneath, beneathWords ?? beneath);
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
