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

    // A node, and the details open under it, are kept for as long as their event is shown and the form stays the
    // same, so the focus on either stays put while a live run's next events arrive.
    private readonly Dictionary<Guid, TimelineNodeButton> buttons = [];

    private readonly Dictionary<Guid, Border> detailBoxes = [];

    private readonly TextBlock empty = Styles.Text(null, "DvTextSecondary");

    private readonly StackPanel earlier = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace3") };

    private readonly TextBlock hidden = Styles.Text(null, "DvTextSecondary");

    // The lanes form: a grid of columns that share the width, inside a region the arrow keys move through.
    private Grid? grid;

    private ArrowKeyZone? zone;

    private ScrollViewer? lanes;

    private double smallestLanes;

    // The list form: one section for each node.
    private StackPanel? list;

    // Kept by event, not by row: a late event with an earlier start moves rows, and must not move the selection.
    private Guid? selected;

    private Guid? opened;

    private bool moveFocus;

    private bool drawing;

    // How many of the newest events are shown. It grows a page at a time, and to reach a node a key moved to.
    private int shownCount;

    #endregion

    #region Constructors

    public Timeline()
    {
        Spacing = Tokens.Number("DvSpace3");
        var more = new Button { Content = "Show earlier events" };
        more.Click += (_, _) =>
        {
            shownCount = Math.Max(shownCount, PageSize) + PageSize;
            Draw();
        };
        hidden.VerticalAlignment = VerticalAlignment.Center;
        earlier.Children.Add(hidden);
        earlier.Children.Add(more);
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
        if (drawing)
        {
            return;
        }

        drawing = true;
        try
        {
            DrawShown();
        }
        finally
        {
            drawing = false;
        }
    }

    private void DrawShown()
    {
        // The node the keyboard is on stays the one in the tab order, whichever row its event moves to.
        if (buttons.Values.FirstOrDefault(button => button.FocusState is not FocusState.Unfocused) is { } focused)
        {
            selected = focused.Node.Event.Id;
        }

        if (Model.Nodes.Count is 0)
        {
            empty.Text = EmptyText;
            DropForms();
            ShowOnly(empty);
            return;
        }

        var shown = Shown;
        hidden.Text = Hidden;
        FrameworkElement form = AsList ? List(shown) : Lanes(shown);
        ShowOnly(First > 0 ? [earlier, form] : [form]);

        if (moveFocus && Selected(shown) is { } id && buttons.TryGetValue(id, out var button))
        {
            moveFocus = false;
            Pieces.FocusWhenShown(button, button.TakeFocus);
        }
    }

    // The timeline's own children become exactly these, in this order. One already in place is not taken out.
    private void ShowOnly(params UIElement[] wanted)
    {
        for (var index = Children.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(Children[index]))
            {
                Children.RemoveAt(index);
            }
        }

        for (var index = 0; index < wanted.Length; index++)
        {
            if (index < Children.Count && ReferenceEquals(Children[index], wanted[index]))
            {
                continue;
            }

            Children.Remove(wanted[index]);
            Children.Insert(index, wanted[index]);
        }
    }

    private void DropForms()
    {
        (grid, zone, lanes, list) = (null, null, null, null);
        buttons.Clear();
        detailBoxes.Clear();
    }

    // The same nodes as a list of sentences, each a section that opens to its details.
    private StackPanel List(IReadOnlyList<TimelineNode> shown)
    {
        if (list is null)
        {
            DropForms();
            list = new() { Spacing = Tokens.Number("DvSpace2") };
        }

        AutomationProperties.SetName(list, Label);
        Keyed.Sync(list, shown, node => node.Event.Id.ToString(), Section, ShowSection);
        return list;
    }

    private Disclosure Section(TimelineNode node)
    {
        var id = node.Event.Id;
        var section = new Disclosure();
        section.RegisterPropertyChangedCallback(Disclosure.OpenProperty, (_, _) =>
        {
            if (drawing)
            {
                return;
            }

            opened = section.Open ? id : opened == id ? null : opened;
            Draw();
        });
        return section;
    }

    private void ShowSection(Disclosure section, TimelineNode node)
    {
        section.Title = Timelines.Sentence(Model, node);
        section.Summary = node.Event.Summary;
        section.Running = node.Event is { State: TimelineState.Live, Kind: TimelineKind.Tool };
        section.Open = opened == node.Event.Id;
        var body = section.Open ? Detail?.Invoke(node) : null;
        if (!ReferenceEquals(section.Body, body))
        {
            section.Body = Placed(body);
        }
    }

    // Lanes are columns that share the width, so three lanes fit a phone; time runs down. Each event takes two
    // grid rows: its node, and under it the row its details open into across every lane.
    private ScrollViewer Lanes(IReadOnlyList<TimelineNode> shown)
    {
        if (grid is null || zone is null || lanes is null)
        {
            DropForms();
            (grid, zone, lanes) = NewLanes();
        }

        Clear(grid, shown);
        zone.Label = Label;
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

            Place(grid, Node(node, isTabStop: node.Event.Id == selectedId), node.Lane, row);

            if (opened == node.Event.Id)
            {
                Place(grid, DetailBox(node), 0, row + 1, columnSpan: Math.Max(1, Model.Lanes.Count));
            }
        }

        // The lanes share the width there is. Only when every lane is at its smallest and they still do not fit does
        // the timeline scroll sideways.
        smallestLanes = (Model.Lanes.Count * Tokens.Number("DvLaneMin")) + ((Model.Lanes.Count - 1) * grid.ColumnSpacing);
        FitLanes();
        return lanes;
    }

    private (Grid Grid, ArrowKeyZone Zone, ScrollViewer Lanes) NewLanes()
    {
        var newGrid = new Grid { ColumnSpacing = Tokens.Number("DvSpace3"), RowSpacing = Tokens.Number("DvSpace2") };
        var newZone = new ArrowKeyZone { Items = newGrid };
        ScrollViewer newLanes = new()
        {
            Content = newZone,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsTabStop = false,
        };
        newLanes.SizeChanged += (_, _) => FitLanes();
        return (newGrid, newZone, newLanes);
    }

    private void FitLanes()
    {
        if (grid is null || lanes is null)
        {
            return;
        }

        var width = Math.Max(lanes.ActualWidth, smallestLanes);
        if (Math.Abs(grid.Width - width) >= 1 || double.IsNaN(grid.Width))
        {
            grid.Width = width;
        }
    }

    // Everything drawn for the run as it was is taken out, except the nodes and the details still shown: they stay
    // in the grid, so the focus on them stays too, and are only moved to their new places.
    private void Clear(Grid lanesGrid, IReadOnlyList<TimelineNode> shown)
    {
        var stays = shown.Select(node => node.Event.Id).ToHashSet();
        foreach (var gone in buttons.Keys.Where(id => !stays.Contains(id)).ToList())
        {
            buttons.Remove(gone);
        }

        foreach (var gone in detailBoxes.Keys.Where(id => id != opened || !stays.Contains(id)).ToList())
        {
            detailBoxes.Remove(gone);
        }

        for (var index = lanesGrid.Children.Count - 1; index >= 0; index--)
        {
            var stay = lanesGrid.Children[index] switch
            {
                TimelineNodeButton button => buttons.ContainsValue(button),
                Border box => detailBoxes.ContainsValue(box),
                _ => false,
            };
            if (!stay)
            {
                lanesGrid.Children.RemoveAt(index);
            }
        }

        lanesGrid.RowDefinitions.Clear();
        lanesGrid.ColumnDefinitions.Clear();
    }

    private static void Place(Grid lanesGrid, FrameworkElement element, int column, int row, int columnSpan = 1, int rowSpan = 1)
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
        Grid.SetColumnSpan(element, columnSpan);
        Grid.SetRowSpan(element, rowSpan);
        if (!lanesGrid.Children.Contains(element))
        {
            lanesGrid.Children.Add(element);
        }
    }

    // A detail kept by its owner, such as an answer on its way, is moved out of where it was shown before.
    private static object? Placed(object? body) => body is FrameworkElement element ? Pieces.Detached(element) : body;

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
        var isOpen = opened == node.Event.Id;
        if (buttons.TryGetValue(node.Event.Id, out var button))
        {
            button.Show(node, isOpen, EdgeOf(node, isOpen));
        }
        else
        {
            button = new TimelineNodeButton(node, isOpen, EdgeOf(node, isOpen));
            ArrowKeyZone.SetIsItem(button, true);
            var kept = button;
            button.Click += (_, _) =>
            {
                var id = kept.Node.Event.Id;
                (selected, opened, moveFocus) = (id, opened == id ? null : id, true);
                Draw();
            };
            button.KeyDown += (_, key) => OnKey(kept.Node, key);
            buttons[node.Event.Id] = button;
        }

        button.IsTabStop = isTabStop;
        AutomationProperties.SetName(button, Timelines.Sentence(Model, node));
        AutomationProperties.SetItemStatus(button, isOpen ? "Expanded" : "Collapsed");
        return button;
    }

    private Border DetailBox(TimelineNode node)
    {
        if (!detailBoxes.TryGetValue(node.Event.Id, out var box))
        {
            box = new()
            {
                Child = new ContentPresenter(),
                Padding = new(Tokens.Number("DvSpace3")),
                BorderThickness = new(Tokens.Number("DvBorder")),
            };
            detailBoxes[node.Event.Id] = box;
        }

        (box.BorderBrush, box.Background) = (Tokens.Brush(this, "DvLine"), Tokens.Brush(this, "DvBg1"));
        var presenter = (ContentPresenter)box.Child;
        var body = Detail?.Invoke(node);
        if (!ReferenceEquals(presenter.Content, body))
        {
            presenter.Content = Placed(body);
        }

        return box;
    }

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

    #region Focus Check

    /// <summary>Opens one node without moving focus, for the check that a later event leaves that focus where it is.</summary>
    internal void OpenForCheck(Guid id)
    {
        (selected, opened, moveFocus) = (id, id, false);
        Draw();
    }

    /// <summary>The details shown under a node, when that node is open.</summary>
    internal UIElement? DetailForCheck(Guid id) =>
        detailBoxes.TryGetValue(id, out var box) && box.Child is ContentPresenter presenter
            ? presenter.Content as UIElement
            : null;

    #endregion
}
