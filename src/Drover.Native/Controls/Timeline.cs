namespace Drover.Native.Controls;

/// <summary>
/// A run as a timeline: one lane for each bot, headed by its head and name, every event a node in time order down
/// the page. Work handed to another bot branches across to that bot's lane in violet. The node being worked on
/// carries its live state; nothing else moves. One tab stop: the arrow keys move between nodes, Enter opens one.
/// Each node is named as a sentence that starts with its bot, so a screen reader reads the same list the list form
/// shows. The lane's hue is never the only way to tell lanes apart. A long run shows its newest events and pages
/// back.
/// </summary>
public sealed class Timeline : StackPanel
{
    #region Private Fields

    private const int HueSteps = 12;

    private const double RailHueShare = 0.55;

    private readonly Dictionary<Guid, TimelineNodeButton> buttons = [];

    // Kept by event, not by row: a late event with an earlier start moves rows, and must not move the selection.
    private Guid? selected;

    private Guid? opened;

    private bool moveFocus;

    // How many of the newest events are shown. It grows a page at a time, and to reach a node a key moved to.
    private int shownCount;

    #endregion

    #region Constructors

    public Timeline()
    {
        Spacing = Tokens.Number("DvSpace3");
        this.WhenShownOrRethemed(Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty ModelProperty =
        Dp.Register<Timeline, TimelineModel>(nameof(Model), TimelineModel.Empty, timeline => timeline.Draw());

    public static readonly DependencyProperty LabelProperty = Dp.Register<Timeline, string>(nameof(Label), string.Empty, timeline => timeline.Draw());

    public static readonly DependencyProperty EmptyTextProperty =
        Dp.Register<Timeline, string>(nameof(EmptyText), string.Empty, timeline => timeline.Draw());

    public static readonly DependencyProperty AsListProperty = Dp.Register<Timeline, bool>(nameof(AsList), false, timeline => timeline.Draw());

    public static readonly DependencyProperty PageSizeProperty = Dp.Register<Timeline, int>(nameof(PageSize), 200, timeline => timeline.Draw());

    public TimelineModel Model
    {
        get => (TimelineModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>What this timeline is, such as "What happened in this run".</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>What to say when there is nothing to show, such as "Nothing has run in this conversation yet."</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>The same nodes as a list of sentences, each opening to its details.</summary>
    public bool AsList
    {
        get => (bool)GetValue(AsListProperty);
        set => SetValue(AsListProperty, value);
    }

    /// <summary>How many of the newest events show at first, and how many more each "Show earlier events" adds.</summary>
    public int PageSize
    {
        get => (int)GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    /// <summary>A node's details: arguments, output as it streams, result, duration, cost.</summary>
    public Func<TimelineNode, object?>? Detail { get; set; }

    // The row of the first event shown. Everything before it waits behind "Show earlier events".
    private int First => Math.Max(0, Model.Nodes.Count - Math.Max(shownCount, PageSize));

    private IReadOnlyList<TimelineNode> Shown => First is 0 ? Model.Nodes : [.. Model.Nodes.Skip(First)];

    private string Hidden => First is 1 ? "1 earlier event is not shown." : $"{First} earlier events are not shown.";

    #endregion

    #region Drawing

    private void Draw()
    {
        // A run that is still going is drawn again as its events arrive: the node the keyboard is on keeps the focus.
        moveFocus |= buttons.Values.Any(button => button.FocusState is not FocusState.Unfocused);
        Children.Clear();
        buttons.Clear();

        if (Model.Nodes.Count is 0)
        {
            Children.Add(Styles.Text(EmptyText, "DvTextSecondary"));
            return;
        }

        if (First > 0)
        {
            Children.Add(Earlier());
        }

        var shown = Shown;
        Children.Add(AsList ? List(shown) : Lanes(shown));

        if (moveFocus && Selected(shown) is { } id && buttons.TryGetValue(id, out var button))
        {
            moveFocus = false;
            button.Loaded += (_, _) => button.TakeFocus();
        }
    }

    private StackPanel Earlier()
    {
        var more = new Button { Content = "Show earlier events" };
        more.Click += (_, _) =>
        {
            shownCount = Math.Max(shownCount, PageSize) + PageSize;
            Draw();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace3") };
        var hint = Styles.Text(Hidden, "DvTextSecondary");
        hint.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(hint);
        row.Children.Add(more);
        return row;
    }

    private StackPanel List(IReadOnlyList<TimelineNode> shown)
    {
        var list = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        AutomationProperties.SetName(list, Label);

        foreach (var node in shown)
        {
            var section = new Disclosure
            {
                Title = Timelines.Sentence(Model, node),
                Summary = node.Event.Summary,
                Running = node.Event is { State: TimelineState.Live, Kind: TimelineKind.Tool },
                Open = opened == node.Event.Id,
            };
            section.Body = section.Open ? Detail?.Invoke(node) : null;
            section.RegisterPropertyChangedCallback(Disclosure.OpenProperty, (_, _) =>
            {
                opened = section.Open ? node.Event.Id : null;
                Draw();
            });
            list.Children.Add(section);
        }

        return list;
    }

    // Lanes are columns that share the width, so three lanes fit a phone; time runs down. Each event takes two
    // grid rows: its node, and under it the row its details open into across every lane.
    private ScrollViewer Lanes(IReadOnlyList<TimelineNode> shown)
    {
        var grid = new Grid { ColumnSpacing = Tokens.Number("DvSpace3"), RowSpacing = Tokens.Number("DvSpace2") };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        foreach (var _ in shown)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }

        foreach (var (bot, lane) in Model.Lanes.Select((bot, lane) => (bot, lane)))
        {
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = Tokens.Number("DvLaneMin") });
            Place(grid, Head(bot, lane), lane, row: 0);
            Place(grid, Rail(bot), lane, row: 1, rowSpan: Math.Max(1, shown.Count * 2));
        }

        var selectedId = Selected(shown);
        foreach (var node in shown)
        {
            var row = ((node.Row - First) * 2) + 1;

            if (node.BranchLane is { } branch)
            {
                var (from, to) = (Math.Min(node.Lane, branch), Math.Max(node.Lane, branch));
                Place(grid, Branch(node), from, row, columnSpan: Math.Max(1, to - from));
            }

            var button = Node(node, isTabStop: node.Event.Id == selectedId);
            buttons[node.Event.Id] = button;
            Place(grid, button, node.Lane, row);

            if (opened == node.Event.Id)
            {
                Place(grid, DetailBox(node), 0, row + 1, columnSpan: Math.Max(1, Model.Lanes.Count));
            }
        }

        var zone = new ArrowKeyZone { Label = Label, Items = grid };
        ScrollViewer lanes = new()
        {
            Content = zone,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsTabStop = false,
        };

        // The lanes share the width there is. Only when every lane is at its smallest and they still do not fit does
        // the timeline scroll sideways.
        var smallest = (Model.Lanes.Count * Tokens.Number("DvLaneMin")) + ((Model.Lanes.Count - 1) * grid.ColumnSpacing);
        lanes.SizeChanged += (_, _) =>
        {
            var width = Math.Max(lanes.ActualWidth, smallest);
            if (Math.Abs(grid.Width - width) >= 1 || double.IsNaN(grid.Width))
            {
                grid.Width = width;
            }
        };
        return lanes;
    }

    private static void Place(Grid grid, FrameworkElement element, int column, int row, int columnSpan = 1, int rowSpan = 1)
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
        Grid.SetColumnSpan(element, columnSpan);
        Grid.SetRowSpan(element, rowSpan);
        grid.Children.Add(element);
    }

    // A lane's head shows what its bot is doing, as the bot's head does everywhere else. It repeats what the
    // nodes say, so it is not read out a second time.
    private Border Head(TimelineBot bot, int lane)
    {
        var name = Styles.Text(bot.Name);
        name.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;

        var row = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(name, 1);
        row.Children.Add(new Avatar { BotName = bot.Name, Hue = BotIdentity.Hue(bot.Id), Size = 24, State = HeadState(lane) });
        row.Children.Add(name);
        AutomationProperties.SetAccessibilityView(row, AccessibilityView.Raw);

        return new() { Child = row, Padding = new(0, Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2")), Background = Tokens.Brush(this, "DvBg1") };
    }

    // The lane's line, behind its nodes, in the bot's identity hue mixed toward the decorative line.
    private Border Rail(TimelineBot bot)
    {
        var hue = ((BotIdentity.Hue(bot.Id) % HueSteps) + HueSteps) % HueSteps;
        var mixed = Tokens.Color(this, $"DvHue{hue}").ToRgb().MixWith(Tokens.Color(this, "DvLine").ToRgb(), RailHueShare);

        return new()
        {
            Width = Tokens.Number("DvBorder"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(Tokens.Number("DvSpace3"), 0, 0, 0),
            Background = new SolidColorBrush(mixed.ToColor()),
        };
    }

    // Work handed across: a violet line from the node that asked to the rail of the lane that got the work. Work
    // that is still out pulses once along it, when it is handed over.
    private FrameworkElement Branch(TimelineNode node)
    {
        // It spans the columns up to the lane that got the work, then reaches over the gap and the rail's inset.
        var reach = new Thickness(Tokens.Number("DvSpace3"), 0, -2 * Tokens.Number("DvSpace3"), 0);
        var height = Tokens.Number("DvBorder");

        if (node.Event is { Kind: TimelineKind.Delegation, State: TimelineState.Live })
        {
            return new LiveLight { State = LiveState.Passing, Height = height, VerticalAlignment = VerticalAlignment.Center, Margin = reach };
        }

        return new Border
        {
            Height = height,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = reach,
            Background = Tokens.Brush(this, "DvViolet"),
        };
    }

    private TimelineNodeButton Node(TimelineNode node, bool isTabStop)
    {
        var button = new TimelineNodeButton(node, opened == node.Event.Id, EdgeOf(node, opened == node.Event.Id)) { IsTabStop = isTabStop };
        ArrowKeyZone.SetIsItem(button, true);
        AutomationProperties.SetName(button, Timelines.Sentence(Model, node));
        AutomationProperties.SetItemStatus(button, opened == node.Event.Id ? "Expanded" : "Collapsed");
        button.Click += (_, _) =>
        {
            (selected, opened, moveFocus) = (node.Event.Id, opened == node.Event.Id ? null : node.Event.Id, true);
            Draw();
        };
        button.KeyDown += (_, key) => OnKey(node, key);
        return button;
    }

    private Border DetailBox(TimelineNode node) => new()
    {
        Child = new ContentPresenter { Content = Detail?.Invoke(node) },
        Padding = new(Tokens.Number("DvSpace3")),
        BorderThickness = new(Tokens.Number("DvBorder")),
        BorderBrush = Tokens.Brush(this, "DvLine"),
        Background = Tokens.Brush(this, "DvBg1"),
    };

    #endregion

    #region State

    // The one node in the tab order: the one last moved to, or the first shown.
    private Guid? Selected(IReadOnlyList<TimelineNode> shown) =>
        selected is { } id && shown.Any(node => node.Event.Id == id) ? id : shown.Count > 0 ? shown[0].Event.Id : null;

    // The node's edge: ion while it is live, violet while work is out with another bot, then the result's color.
    private static string? EdgeOf(TimelineNode node, bool isOpen) => node.Event switch
    {
        { State: TimelineState.Live, Kind: TimelineKind.Delegation } => "DvViolet",
        { State: TimelineState.Live, Kind: TimelineKind.Tool } => isOpen ? "DvText3" : null,
        { State: TimelineState.Live } => "DvIon",
        { State: TimelineState.Failed } => "DvDanger",
        { State: TimelineState.Waiting } => "DvWarn",
        { Kind: TimelineKind.Delegation or TimelineKind.Answer } => "DvViolet",
        _ => isOpen ? "DvText3" : null,
    };

    private AvatarState HeadState(int lane)
    {
        var live = Model.Nodes.Where(node => node.Lane == lane && node.Event.State is TimelineState.Live).Select(node => node.Event).ToList();
        return live switch
        {
            _ when live.Any(item => item.Kind is TimelineKind.Tool) => AvatarState.Tool,
            _ when live.Any(item => item is { Kind: TimelineKind.Step, Writing: true }) => AvatarState.Writing,
            _ when live.Any(item => item.Kind is TimelineKind.Step) => AvatarState.Thinking,
            _ => AvatarState.Rest,
        };
    }

    private void OnKey(TimelineNode node, KeyRoutedEventArgs key)
    {
        var name = key.Key switch
        {
            VirtualKey.Down => "ArrowDown",
            VirtualKey.Up => "ArrowUp",
            VirtualKey.Left => "ArrowLeft",
            VirtualKey.Right => "ArrowRight",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            _ => null,
        };
        if (name is null)
        {
            return;
        }

        // The key is taken either way, so the page behind never scrolls on it.
        key.Handled = true;
        var row = Timelines.Move(Model, node.Row, name);
        var next = Model.Nodes[row].Event.Id;
        if (next == node.Event.Id)
        {
            return;
        }

        if (row < First)
        {
            shownCount = Model.Nodes.Count - row;
        }

        (selected, moveFocus) = (next, true);
        Draw();
    }

    #endregion
}
