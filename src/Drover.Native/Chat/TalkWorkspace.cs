namespace Drover.Native.Chat;

/// <summary>
/// The workspace as it is drawn: the crew, the main conversation with the chosen bot, and what is open beside it.
/// A narrow window shows one at a time; a wide one shows the crew beside the conversation; a wider one shows the
/// rail beside the main conversation. It draws what its view model says and decides nothing itself.
/// </summary>
internal sealed class TalkWorkspace : Grid
{
    #region Private Fields

    private readonly SessionGate gate = new();

    private readonly Grid shell = new();

    private readonly Border listBox = new();

    private readonly Grid mainBox = new();

    private readonly Border railBox = new();

    private readonly Border listHeader = new();

    private readonly ContentPresenter crewNotice = new();

    private readonly CrewList crew = new();

    private readonly TextBlock crewHeading = Styles.Text("Conversations", "DvTextH1");

    private readonly Border listFooter = new();

    private readonly PhoneNav dock = new();

    private readonly NavLink[] footerLinks = [new("Bots", Addresses.Bots), new("Tools", Addresses.Tools)];

    private readonly Border mainHeader = new();

    private readonly Grid mainHeaderRow = new() { ColumnSpacing = Tokens.Number("DvSpace2"), RowSpacing = Tokens.Number("DvSpace2") };

    private readonly IconLink back = new() { Icon = IconName.ArrowLeft, Label = "Back to the crew", Address = Addresses.Home };

    private readonly Avatar head = new() { Size = 48, VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock title = Styles.Text(null, "DvTextH3");

    private readonly Badge paused = new() { Text = "Paused", Kind = BadgeKind.Warn };

    private readonly ContentPresenter more = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly FlowPanel beside = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly ContentPresenter mainBody = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };

    private readonly Border railHeader = new();

    private readonly IconLink railUp = new();

    private readonly TextBlock railBot = Styles.Text(null, "DvTextMono");

    private readonly TextBlock railSlash = Styles.Text("/", "DvTextMono");

    private readonly TextBlock railTitle = Styles.Text(null);

    private readonly ContentPresenter railBody = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };

    private readonly Dictionary<SideView, FacetLink> ways = [];

    private readonly TalkViewModel model;

    private BotNotifyMenu? menu;

    private Guid? menuBot;

    private string? menuItems;

    private string? layout;

    private string? mainShown;

    private string? railShown;

    private string? crewShown;

    private bool drawQueued;

    private bool focusClose;

    #endregion

    #region Constructors

    public TalkWorkspace(TalkViewModel model)
    {
        this.model = model;
        shell.ColumnDefinitions.Add(new());
        shell.ColumnDefinitions.Add(new());
        shell.ColumnDefinitions.Add(new());
        BuildList();
        BuildMain();
        BuildRail();
        SetColumn(mainBox, 1);
        SetColumn(railBox, 2);
        shell.Children.Add(listBox);
        shell.Children.Add(mainBox);
        shell.Children.Add(railBox);
        gate.Body = shell;
        Children.Add(gate);

        model.PropertyChanged += (_, _) => QueueDraw();
        model.RailCloseFocusRequested += () =>
        {
            focusClose = true;
            QueueDraw();
        };
        SizeChanged += (_, _) => Fit();
        this.WhenShownOrRethemed(Paint);
        Draw();
    }

    #endregion

    #region Properties

    /// <summary>Raised when the words for the window's title changed.</summary>
    public event Action? TitleChanged;

    #endregion

    #region Building

    private void BuildList()
    {
        var word = Styles.Text("Drover", "DvTextH3");
        word.CharacterSpacing = (int)Tokens.Dictionary["DvTracking"];
        word.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(word, AutomationHeadingLevel.None);
        var wordmark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2"), VerticalAlignment = VerticalAlignment.Center };
        wordmark.Children.Add(new DroverMark { VerticalAlignment = VerticalAlignment.Center });
        wordmark.Children.Add(word);
        Bar(listHeader, wordmark);

        // The crew is the page's heading on the start page of a narrow window. It is read out and takes no room.
        crewHeading.Opacity = 0;
        crewHeading.Height = 0;
        var crewColumn = new StackPanel();
        crewColumn.Children.Add(crewHeading);
        crewColumn.Children.Add(crewNotice);
        crewColumn.Children.Add(crew);
        var crewScroll = new ScrollViewer
        {
            Content = crewColumn,
            Padding = new(Tokens.Number("DvSpace2")),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            IsTabStop = false,
        };
        AutomationProperties.SetName(crewScroll, "Crew");
        AutomationProperties.SetLandmarkType(crewScroll, AutomationLandmarkType.Navigation);

        var footer = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        foreach (var link in footerLinks)
        {
            link.Padding = new(Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2"), 0);
            SetColumn(link, footer.Children.Count);
            footer.Children.Add(link);
        }

        var account = new AccountMenu { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        SetColumn(account, 2);
        footer.Children.Add(account);
        listFooter.Child = footer;
        listFooter.Padding = Tokens.Space("DvSpace4", "DvSpace3");
        listFooter.BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);

        var column = new Grid();
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        column.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        SetRow(crewScroll, 1);
        SetRow(listFooter, 2);
        SetRow(dock, 3);
        column.Children.Add(listHeader);
        column.Children.Add(crewScroll);
        column.Children.Add(listFooter);
        column.Children.Add(dock);
        listBox.Child = column;
    }

    private void BuildMain()
    {
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.VerticalAlignment = VerticalAlignment.Center;
        paused.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(beside, "Beside the conversation");
        AutomationProperties.SetLandmarkType(beside, AutomationLandmarkType.Navigation);

        (SideView Side, Func<Guid, string> Address)[] links =
        [
            (SideView.Activity, Addresses.Activity),
            (SideView.Summary, Addresses.Summary),
            (SideView.Memory, Addresses.Memory),
            (SideView.Screen, Addresses.Screen),
            (SideView.Files, Addresses.Files),
            (SideView.List, Addresses.SideChats),
        ];
        foreach (var (side, _) in links)
        {
            var way = Way();
            ways[side] = way;
            beside.Children.Add(way);
        }

        // Back, the head, the name, that the bot is paused, the menu, room, and the ways beside the conversation.
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto })
        {
            mainHeaderRow.ColumnDefinitions.Add(new() { Width = width });
        }

        mainHeaderRow.RowDefinitions.Add(new() { Height = GridLength.Auto });
        mainHeaderRow.RowDefinitions.Add(new() { Height = GridLength.Auto });
        FrameworkElement[] parts = [back, head, title, paused, more];
        foreach (var part in parts)
        {
            SetColumn(part, mainHeaderRow.Children.Count);
            mainHeaderRow.Children.Add(part);
        }

        mainHeaderRow.Children.Add(beside);
        Bar(mainHeader, mainHeaderRow);

        mainBox.RowDefinitions.Add(new() { Height = GridLength.Auto });
        mainBox.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var yard = new Yard();
        SetRowSpan(yard, 2);
        SetRow(mainBody, 1);
        mainBox.Children.Add(yard);
        mainBox.Children.Add(mainHeader);
        mainBox.Children.Add(mainBody);
        AutomationProperties.SetLandmarkType(mainBox, AutomationLandmarkType.Main);
    }

    private void BuildRail()
    {
        railBot.TextTrimming = railTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        railBot.TextWrapping = railTitle.TextWrapping = TextWrapping.NoWrap;
        railBot.VerticalAlignment = railSlash.VerticalAlignment = railTitle.VerticalAlignment = VerticalAlignment.Center;
        railTitle.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        AutomationProperties.SetHeadingLevel(railTitle, AutomationHeadingLevel.Level1);
        AutomationProperties.SetAccessibilityView(railSlash, AccessibilityView.Raw);

        var row = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        FrameworkElement[] parts = [railUp, railBot, railSlash, railTitle];
        foreach (var part in parts)
        {
            SetColumn(part, row.Children.Count);
            row.Children.Add(part);
        }

        Bar(railHeader, row);

        var column = new Grid();
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        column.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        SetRow(railBody, 1);
        column.Children.Add(railHeader);
        column.Children.Add(railBody);
        railBox.Child = column;
        AutomationProperties.SetLandmarkType(railBox, AutomationLandmarkType.Custom);
    }

    // One height for all three bars, set by the bot's head in the conversation's header, so their lines meet.
    private static void Bar(Border bar, FrameworkElement content)
    {
        bar.Child = content;
        bar.MinHeight = Tokens.Number("DvAvatar48") + (2 * Tokens.Number("DvSpace2")) + Tokens.Number("DvBorder");
        bar.Padding = Tokens.Space("DvSpace4", "DvSpace2");
        bar.BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder"));
        content.VerticalAlignment = VerticalAlignment.Center;
    }

    // A way to what opens beside the conversation: a chip that is framed lighter while that thing is open.
    private static FacetLink Way() => new()
    {
        Fill = "DvBg2",
        HoverFill = "DvBg3",
        CurrentFill = "DvBg3",
        Edge = "DvText3",
        HoverEdge = "DvText2",
        CurrentEdge = "DvText2",
        Ink = "DvText",
        HorizontalAlignment = HorizontalAlignment.Left,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0),
    };

    #endregion

    #region Drawing

    // The screen is drawn once for everything that changed since it was last drawn.
    private void QueueDraw()
    {
        if (!drawQueued)
        {
            drawQueued = true;
            DispatcherQueue.TryEnqueue(Draw);
        }
    }

    private void Draw()
    {
        drawQueued = false;
        DrawCrew();
        DrawMain();
        DrawRail();
        Fit();
        FitMenu();
        Paint();
        TitleChanged?.Invoke();

        if (focusClose)
        {
            focusClose = false;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, railUp.TakeFocus);
        }
    }

    private void DrawCrew()
    {
        var ready = model.NotReady is WorkspaceState.Ready;
        AutomationProperties.SetAccessibilityView(crewHeading, model.At is "crew" ? AccessibilityView.Content : AccessibilityView.Raw);
        crew.Visibility = Shown(ready);

        if (ready)
        {
            crew.Crew = model.Crew;
            crew.SelectedId = model.Shown?.Id;
            crew.Working = model.WorkingBots;
            crew.ForYou = model.ForYou;
            crew.Handoffs = model.Handoffs;
        }

        // A narrow window shows what stands in for the workspace in the list; a wide one shows it in the main area.
        var key = ready ? null : NoticeKey();
        if (key != crewShown)
        {
            crewShown = key;
            crewNotice.Content = key is null ? null : NotReady();
        }
    }

    private void DrawMain()
    {
        var shown = model.Shown;
        var conversation = model.NotReady is WorkspaceState.Ready && !model.FormerMissing && shown is not null && model.Address.FormerThreadId is null;
        mainHeader.Visibility = Shown(conversation);

        var key = conversation ? $"pane:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(model.MainPane)}" : NoticeKey();
        if (key != mainShown)
        {
            mainShown = key;
            mainBody.Content = conversation ? new ConversationPane { Model = model.MainPane } : Centered(NotReady());
        }

        if (!conversation || shown is null)
        {
            return;
        }

        head.BotName = shown.Name;
        head.Hue = BotIdentity.Hue(shown.Id);
        head.State = model.MainBusy ? AvatarState.Thinking : AvatarState.Rest;
        title.Text = shown.Name;
        AutomationProperties.SetHeadingLevel(title, model.At is "main" ? AutomationHeadingLevel.Level1 : AutomationHeadingLevel.Level2);
        paused.Visibility = Shown(ChatRows.IsPaused(shown));
        DrawMenu(shown);
        DrawWays(shown);
    }

    // The same ways as the links of a wide window, for a window too narrow to show them.
    private void DrawMenu(BotSummary shown)
    {
        var items = $"{model.OffersScreen}|{model.SideChatsLabel}";
        if (menu is not null && menuBot == shown.Id && items == menuItems)
        {
            menu.Bot = shown;
            return;
        }

        (string Label, string Address)[] wanted =
        [
            ("Summary", Addresses.Summary(shown.Id)),
            ("Memory", Addresses.Memory(shown.Id)),
            .. model.OffersScreen ? [("Screen", Addresses.Screen(shown.Id))] : Array.Empty<(string, string)>(),
            ("Files", Addresses.Files(shown.Id)),
            (model.SideChatsLabel, Addresses.SideChats(shown.Id)),
        ];

        // A different bot gets a menu of its own, which asks afresh whether that bot may notify the person.
        if (menu is null || menuBot != shown.Id)
        {
            menu = new() { VerticalAlignment = VerticalAlignment.Center };
            more.Content = menu;
        }

        (menuBot, menuItems) = (shown.Id, items);
        menu.Items = [.. wanted.Select(Item)];
        menu.Bot = shown;
    }

    private MenuItem Item((string Label, string Address) way)
    {
        var item = new MenuItem { Content = way.Label };
        item.Selected += (_, _) => model.OpenSide(way.Address);
        return item;
    }

    private void DrawWays(BotSummary shown)
    {
        Set(SideView.Activity, "Activity", Addresses.Activity(shown.Id), model.Side is SideView.Activity);
        Set(SideView.Summary, "Summary", Addresses.Summary(shown.Id), model.Side is SideView.Summary);
        Set(SideView.Memory, "Memory", Addresses.Memory(shown.Id), model.Side is SideView.Memory);
        Set(SideView.Screen, "Screen", Addresses.Screen(shown.Id), model.Side is SideView.Screen);
        Set(SideView.Files, "Files", Addresses.Files(shown.Id), model.Side is SideView.Files);
        Set(SideView.List, model.SideChatsLabel, Addresses.SideChats(shown.Id), model.Side is SideView.List or SideView.New or SideView.Chat);
        ways[SideView.Screen].Visibility = Shown(model.OffersScreen);

        void Set(SideView side, string label, string address, bool open)
        {
            var way = ways[side];
            if (way.Content as string != label)
            {
                way.Content = label;
            }

            way.Address = address;
            way.ShowCurrent(open, "Open");
        }
    }

    private void DrawRail()
    {
        if (!model.CanShowRail || model.Shown is not { } shown)
        {
            railShown = null;
            railBody.Content = null;
            return;
        }

        AutomationProperties.SetName(railBox, model.RailName);
        railUp.Icon = model.RailCloses ? IconName.Close : IconName.ArrowLeft;
        railUp.Label = model.RailCloses ? model.CloseRailName : model.AllSideChatsName;
        railUp.Address = model.RailCloses ? Addresses.Talk(shown.Id) : Addresses.SideChats(shown.Id);
        railBot.Text = model.Side is SideView.Work ? model.WorkName : shown.Name;
        railTitle.Text = model.SideTitle;

        // What is beside the conversation is made again for a different bot, and a conversation for a different one.
        var pane = model.Side is SideView.Work ? model.WorkPane : model.SidePane;
        var key = $"{model.Side}:{shown.Id}:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pane)}:{model.WorkNotice is not null}";
        if (key != railShown)
        {
            railShown = key;
            railBody.Content = Beside(shown);
        }

        Tell(shown);
    }

    private FrameworkElement Beside(BotSummary shown) => model.Side switch
    {
        SideView.Summary => new SummaryPanel(),
        SideView.Screen => Screen(),
        SideView.Activity => new ActivityPanel(),
        SideView.Memory => new MemoryRail(),
        SideView.Files => new ExchangedFiles(),
        SideView.Work when model.WorkNotice is { } notice => Body(Empty(notice)),
        SideView.Work => new ConversationPane { Model = model.WorkPane },
        SideView.List => SideChats(shown),
        _ => new ConversationPane { Model = model.SidePane },
    };

    private ScreenPanel Screen()
    {
        var screen = new ScreenPanel();
        screen.OnHandedBack += (_, toolCallId) => model.HandedBack(toolCallId);
        return screen;
    }

    // Each thing beside the conversation is told what the screen now says: the bot, and what tells it to read again.
    private void Tell(BotSummary shown)
    {
        switch (railBody.Content)
        {
            case SummaryPanel summary:
                (summary.Bot, summary.Version) = (shown, model.SummaryVersion);
                break;
            case ScreenPanel screen:
                (screen.Bot, screen.Takeover) = (shown, model.HeldTakeover);
                break;
            case ActivityPanel activity:
                (activity.Bot, activity.Bots, activity.RunId, activity.Version) = (shown, model.Bots!, model.Address.ActivityRunId, model.ActivityVersion);
                break;
            case MemoryRail memory:
                (memory.Bot, memory.Version) = (shown, model.ConversationVersion);
                break;
            case ExchangedFiles files:
                (files.Bot, files.Version) = (shown, model.ConversationVersion);
                break;
            case ScrollViewer { Content: StackPanel { Tag: nameof(SideChatList) } column }:
                TellSideChats(column, shown);
                break;
        }
    }

    private ScrollViewer SideChats(BotSummary shown)
    {
        var hint = Styles.Text(null, "DvTextSecondary");
        var hintBox = new Border { Child = hint, Padding = Tokens.Space("DvSpace3", "DvSpace2") };
        var chats = new SideChatList();

        var add = Way();
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        content.Children.Add(new Icon { NameOfIcon = IconName.Plus, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = "New side chat", VerticalAlignment = VerticalAlignment.Center });
        add.Content = content;
        add.Margin = new(Tokens.Number("DvSpace3"), 0, 0, 0);
        AutomationProperties.SetName(add, "New side chat");

        var column = new StackPanel { Tag = nameof(SideChatList), Spacing = Tokens.Number("DvSpace3") };
        column.Children.Add(hintBox);
        column.Children.Add(chats);
        column.Children.Add(add);
        TellSideChats(column, shown);
        return Body(column);
    }

    private void TellSideChats(StackPanel column, BotSummary shown)
    {
        ((TextBlock)((Border)column.Children[0]).Child).Text = model.SideChatsHint;
        var chats = (SideChatList)column.Children[1];
        var listed = model.SideChats;
        chats.Visibility = Shown(listed.Count > 0);
        chats.Chats = listed;
        ((FacetLink)column.Children[2]).Address = Addresses.NewSideChat(shown.Id);
    }

    private static ScrollViewer Body(FrameworkElement content) => new()
    {
        Content = content,
        Padding = new(Tokens.Number("DvSpace2")),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Disabled,
        IsTabStop = false,
    };

    #endregion

    #region What Stands In For The Workspace

    private string? NoticeKey() => model.Notice is { } notice ? $"{notice.Title}|{notice.Cause}" : model.NotReady.ToString();

    private FrameworkElement NotReady()
    {
        if (model.Notice is { } notice)
        {
            return Empty(notice);
        }

        var hint = Styles.Text(TalkViewModel.LoadingHint, "DvTextSecondary");
        AutomationProperties.SetLiveSetting(hint, AutomationLiveSetting.Polite);
        return new Border { Child = hint, Padding = Tokens.Space("DvSpace3", "DvSpace2") };
    }

    private EmptyState Empty(EmptyNotice notice)
    {
        var empty = new EmptyState { Title = notice.Title, Cause = notice.Cause, ActionLabel = notice.ActionLabel, ActionHref = notice.ActionHref };
        if (notice.ActionHref is null)
        {
            empty.Action += async (_, _) => await model.Retry();
        }

        return empty;
    }

    private static Border Centered(FrameworkElement content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        return new() { Child = content, Padding = new(Tokens.Number("DvSpace4")) };
    }

    #endregion

    #region Fitting The Window

    // Which of the three stand side by side follows the window's width, and on a narrow one where the person is.
    private void Fit()
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var phone = width < (double)Styles.Dictionary["DvBreakPhone"];
        var crewBeside = width >= (double)Styles.Dictionary["DvBreakCrew"];
        var railBeside = width >= (double)Styles.Dictionary["DvBreakRail"];
        var rail = model.CanShowRail;
        var at = model.At;
        var showList = crewBeside || at is "crew";
        var showMain = crewBeside ? !rail || railBeside || at is not "side" : at is "main";
        var showRail = rail && (railBeside || at is "side");
        var listWidth = showList ? crewBeside ? Tokens.Number("DvListWidth") : width : 0;
        var conversation = showMain ? width - listWidth - (showRail ? Tokens.Number("DvRailWidth") : 0) : 0;
        var wrapped = !phone && conversation > 0 && conversation <= (double)Styles.Dictionary["DvBreakBeside"];

        var now = $"{phone}|{crewBeside}|{railBeside}|{showList}|{showMain}|{showRail}|{model.HasWideRail}|{wrapped}|{model.NotReady}";
        if (now == layout)
        {
            return;
        }

        layout = now;
        listBox.Visibility = Shown(showList);
        mainBox.Visibility = Shown(showMain);
        railBox.Visibility = Shown(showRail);

        var star = new GridLength(1, GridUnitType.Star);
        var none = new GridLength(0);
        shell.ColumnDefinitions[0].Width = !showList ? none : crewBeside ? new(Tokens.Number("DvListWidth")) : star;
        shell.ColumnDefinitions[1].Width = showMain ? star : none;
        shell.ColumnDefinitions[2].Width = !showRail ? none : showMain && !model.HasWideRail ? new(Tokens.Number("DvRailWidth")) : star;
        shell.ColumnDefinitions[2].MinWidth = showRail && showMain ? Tokens.Number("DvRailWidth") : 0;

        listBox.BorderThickness = new(0, 0, crewBeside ? Tokens.Number("DvBorder") : 0, 0);
        railBox.BorderThickness = new(showMain ? Tokens.Number("DvBorder") : 0, 0, 0, 0);
        listFooter.Visibility = Shown(!phone);
        dock.Visibility = Shown(phone);
        back.Visibility = Shown(!crewBeside);
        crewNotice.Visibility = Shown(!crewBeside);
        FitHeader(phone, wrapped);
    }

    // The bot's name stays visible until the conversation column can fit all its links beside it: until then the
    // links wrap onto a row of their own. A phone drops them for the menu.
    private void FitHeader(bool phone, bool wrapped)
    {
        var inline = Tokens.Number(phone ? "DvSpace2" : "DvSpace4");
        mainHeader.Padding = new(inline, Tokens.Number("DvSpace2"), inline, Tokens.Number("DvSpace2"));
        beside.Visibility = Shown(!phone);
        beside.Gap = Tokens.Number(wrapped ? "DvSpace1" : "DvSpace2");
        SetRow(beside, wrapped ? 1 : 0);
        SetColumn(beside, wrapped ? 0 : mainHeaderRow.ColumnDefinitions.Count - 1);
        SetColumnSpan(beside, wrapped ? mainHeaderRow.ColumnDefinitions.Count : 1);

        var stretch = phone || wrapped;
        mainHeaderRow.ColumnDefinitions[2].Width = stretch ? new(1, GridUnitType.Star) : GridLength.Auto;
        mainHeaderRow.ColumnDefinitions[5].Width = stretch ? new(0) : new(1, GridUnitType.Star);

    }

    // On a phone the menu's button is a finger wide, since it is the only way to what is beside the conversation.
    private void FitMenu()
    {
        if (menu is not null && ActualWidth > 0)
        {
            menu.Trigger.MinWidth = ActualWidth < (double)Styles.Dictionary["DvBreakPhone"] ? Tokens.Number("DvTargetTouch") : 0;
        }
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private void Paint()
    {
        var line = Tokens.Brush(this, "DvLine");
        listBox.Background = railBox.Background = Tokens.Brush(this, "DvBg1");
        mainHeader.Background = Tokens.Brush(this, "DvBg0");
        listBox.BorderBrush = railBox.BorderBrush = listHeader.BorderBrush = mainHeader.BorderBrush = railHeader.BorderBrush = listFooter.BorderBrush = line;
        railSlash.Foreground = line;
        railBot.Foreground = Tokens.Brush(this, "DvText3");
        dock.ShowWhere(Addresses.Home);
        foreach (var link in footerLinks)
        {
            link.ShowWhere(Addresses.Home);
        }
    }

    #endregion
}
