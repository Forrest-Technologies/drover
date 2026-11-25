namespace Drover.Native.Pages;

/// <summary>
/// A bot's sandbox. Its job: show a person what a bot's sandbox is doing right now and let them take over or restart
/// it. The state and the power controls sit at the top; the screen (for a sandbox with one, <see cref="SandboxScreen"/>)
/// and a terminal are tabs. What it says and does is in <see cref="BotSandboxViewModel"/>.
/// </summary>
public sealed class BotSandboxPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly BotSandboxViewModel model = Make<BotSandboxViewModel>();

    private readonly ScreenHeader header = new() { Title = "Sandbox" };

    private readonly ContentControl under = new() { IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };

    private readonly StackPanel section = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly ContentControl headingHolder = new() { IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };

    private readonly TextBlock heading = Styles.Text(null, "DvTextH2");

    private readonly Badge state = new();

    private readonly Badge botWorking = new() { Kind = BadgeKind.Live, Text = BotSandboxViewModel.BotWorkingWords };

    private readonly TextBlock used = Hint(live: false);

    private readonly FlowPanel stateRow = new() { Gap = Tokens.Number("DvSpace2") };

    private readonly Button pauseOrResume = new();

    private readonly Button rebuild = new() { Content = "Rebuild" };

    private readonly Button turnOff = new() { Kind = ButtonKind.Danger, Content = "Turn off" };

    private readonly TextBlock powerHint = Hint(live: false);

    private readonly FlowPanel hold = new() { Gap = Tokens.Number("DvSpace2") };

    private readonly TextBlock holdWords = Hint(live: false);

    private readonly TextBlock holdStopping = Hint(live: true);

    private readonly Button stopHold = new() { Content = BotSandboxViewModel.StopHoldLabel };

    private readonly StatusNote actionProblem = new() { Kind = NoteKind.Danger };

    private readonly FlowPanel noScreen = new() { Gap = Tokens.Number("DvSpace1") };

    private readonly TextBlock noScreenWords = Hint(live: false);

    private readonly Link settings = new() { Text = BotSandboxViewModel.SettingsLinkLabel };

    private readonly Tabs tabs = new() { Label = "Sandbox" };

    private readonly StackPanel terminal = new() { Spacing = Tokens.Number("DvSpace3"), Padding = new(0, Tokens.Number("DvSpace3"), 0, 0) };

    private readonly Button openTerminal = new() { Content = "Open interactive terminal", HorizontalAlignment = HorizontalAlignment.Left };

    private readonly TextBlock terminalHint = Hint(live: false);

    private readonly StackPanel history = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly Dictionary<SandboxCommandEntry, (string Shown, UIElement Row)> rows = [];

    private readonly Field command = new() { Label = BotSandboxViewModel.CommandLabel };

    private readonly Button run = new() { Kind = ButtonKind.Primary };

    private readonly Dialog rebuilding = new() { Title = BotSandboxViewModel.RebuildTitle };

    private readonly Dialog stoppingHold = new() { Title = BotSandboxViewModel.StopHoldTitle };

    private readonly Dialog turningOff = new() { Title = BotSandboxViewModel.TurnOffTitle };

    private SandboxScreen? screen;

    private (BotSandboxContent Shows, string? Problem)? shown;

    private string shownTitle = string.Empty;

    #endregion

    #region Constructors

    public BotSandboxPage()
    {
        layout.Body.Add(header);
        layout.Body.Add(under);
        Content = layout;

        BuildHead();
        BuildTerminal();
        BuildDialogs();

        model.PropertyChanged += (_, _) => Refresh();
        model.HeadingWantsFocus += FocusHeading;
        Unloaded += async (_, _) => await model.DisposeAsync();
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => model.PageTitle;

    // The tab that shows is named in the address. Choosing one changes the address and nothing else.
    protected override bool StaysOpenWhenQueryChanges => true;

    #endregion

    #region Building

    private void BuildHead()
    {
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        heading.TextWrapping = TextWrapping.Wrap;
        headingHolder.Content = heading;
        headingHolder.LostFocus += (_, _) => headingHolder.IsTabStop = false;

        stateRow.Children.Add(state);
        stateRow.Children.Add(used);

        var power = new SandboxGroup("Power", "DvSpace2");
        power.Add(pauseOrResume, rebuild, turnOff);
        pauseOrResume.Click += async (_, _) => await (model.OffersResume ? model.Resume() : model.Pause());
        rebuild.Click += (_, _) => model.Rebuilding = true;
        turnOff.Click += (_, _) => model.TurningOff = true;

        hold.Children.Add(holdWords);
        hold.Children.Add(stopHold);
        hold.Children.Add(holdStopping);
        holdStopping.Text = BotSandboxViewModel.StoppingNote;
        stopHold.Click += (_, _) => model.StoppingHold = true;

        var head = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        head.Children.Add(headingHolder);
        head.Children.Add(stateRow);
        head.Children.Add(power);
        head.Children.Add(powerHint);
        head.Children.Add(hold);

        noScreen.Children.Add(noScreenWords);
        noScreen.Children.Add(settings);

        tabs.PanelFor = PanelFor;
        tabs.RegisterPropertyChangedCallback(Tabs.ActiveProperty, (_, _) =>
        {
            if (tabs.Active is { } chosen && chosen != model.View)
            {
                model.SelectView(chosen);
            }
        });

        section.Children.Add(head);
        section.Children.Add(actionProblem);
        section.Children.Add(noScreen);
        section.Children.Add(tabs);
        section.Children.Add(rebuilding);
        section.Children.Add(stoppingHold);
        section.Children.Add(turningOff);
    }

    private void BuildTerminal()
    {
        openTerminal.Click += async (_, _) => await model.OpenTerminal();
        AutomationProperties.SetName(history, BotSandboxViewModel.HistoryLabel);
        command.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) => model.Command = command.Value);
        command.Submitted += async (_, _) => await model.RunCommand();
        command.Loaded += (_, _) => SandboxScreen.TuneInput(command, FieldLimits.SandboxCommand, machine: true);
        run.Click += async (_, _) => await model.RunCommand();

        terminal.Children.Add(openTerminal);
        terminal.Children.Add(terminalHint);
        terminal.Children.Add(history);
        terminal.Children.Add(new SandboxForm(command, run));
    }

    private void BuildDialogs()
    {
        rebuilding.Actions = Actions(("Keep this sandbox", ButtonKind.Quiet, () => Later(() => model.Rebuilding = false)), ("Rebuild", ButtonKind.Danger, model.Rebuild));
        stoppingHold.Actions = Actions(("Keep it waiting", ButtonKind.Quiet, () => Later(() => model.StoppingHold = false)), ("Stop the run", ButtonKind.Danger, model.StopHold));
        turningOff.Actions = Actions(("Keep it", ButtonKind.Quiet, () => Later(() => model.TurningOff = false)), ("Turn off", ButtonKind.Danger, model.TurnOff));
        stoppingHold.Body = Styles.Text(BotSandboxViewModel.StopHoldBody, "DvTextParagraph");

        // Escape and the dialog's own Close turn it off, and the model is told.
        rebuilding.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => model.Rebuilding = rebuilding.Open);
        stoppingHold.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => model.StoppingHold = stoppingHold.Open);
        turningOff.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => model.TurningOff = turningOff.Open);
    }

    private static Task Later(Action change)
    {
        change();
        return Task.CompletedTask;
    }

    private static StackPanel Actions(params (string Label, ButtonKind Kind, Func<Task> Act)[] choices)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        foreach (var (label, kind, act) in choices)
        {
            var button = new Button { Content = label, Kind = kind };
            button.Click += async (_, _) => await act();
            row.Children.Add(button);
        }

        return row;
    }

    private static TextBlock Hint(bool live)
    {
        var hint = Styles.Text(null, "DvTextSecondary");
        hint.FontSize = Tokens.Number("DvText13");
        if (live)
        {
            AutomationProperties.SetLiveSetting(hint, AutomationLiveSetting.Polite);
        }

        return hint;
    }

    #endregion

    #region Showing

    protected override async Task Opened(RouteMatch<Type> route)
    {
        if (route.GuidParameter("BotId") is { } botId)
        {
            await model.Open(botId, route.QueryValue("tab"));
        }
    }

    private void Refresh()
    {
        header.BackLabel = model.BackLabel;
        header.BackAddress = Addresses.Bot(model.BotId);
        ShowTitle();

        if (shown != (model.Shows, model.LoadProblem))
        {
            shown = (model.Shows, model.LoadProblem);
            under.Content = model.Shows switch
            {
                BotSandboxContent.LoadFailed => new EmptyState
                {
                    Title = BotSandboxViewModel.LoadFailedTitle,
                    Cause = model.LoadProblem ?? string.Empty,
                    ActionLabel = "Try again",
                    ActionCommand = model.LoadCommand,
                },
                BotSandboxContent.NotPermitted => Empty(
                    BotSandboxViewModel.NotPermittedTitle, BotSandboxViewModel.NotPermittedCause, "Back to conversations", Addresses.Home),
                BotSandboxContent.Missing => Empty(
                    BotSandboxViewModel.MissingTitle, BotSandboxViewModel.MissingCause, "Back to bots", Addresses.Bots),
                BotSandboxContent.NoProvider => Empty(
                    BotSandboxViewModel.NoProviderTitle, BotSandboxViewModel.NoProviderCause, "Open the status page", Addresses.Status),
                BotSandboxContent.NoSandbox => Empty(
                    model.NoSandboxTitle, BotSandboxViewModel.NoSandboxCause, "Open bot settings", Addresses.Bot(model.BotId)),
                BotSandboxContent.Sandbox => section,
                _ => LoadingNote(),
            };
        }

        if (model.Shows is BotSandboxContent.Sandbox)
        {
            ShowSandbox();
        }
    }

    private static EmptyState Empty(string title, string cause, string action, string address) =>
        new() { Title = title, Cause = cause, ActionLabel = action, ActionHref = address };

    private static TextBlock LoadingNote()
    {
        var note = Hint(live: true);
        note.Text = BotSandboxViewModel.LoadingNote;
        return note;
    }

    // The window is named after the bot once it is known.
    private void ShowTitle()
    {
        if (shownTitle != Title && IsLoaded && ((App)Application.Current).MainWindow is { } window)
        {
            shownTitle = Title;
            PlatformShell.SetTitle(window, Title);
        }
    }

    private void ShowSandbox()
    {
        heading.Text = model.Heading;
        state.Text = model.StateWords;
        state.Kind = model.StateBadge;
        ShowBotWorking(model.Status is { BotWorking: true });
        used.Text = model.LastUsed;

        // A paused sandbox offers Resume, the one thing to do with it, where a running one offers Pause.
        pauseOrResume.Content = model.OffersResume ? "Resume" : "Pause";
        pauseOrResume.Kind = model.OffersResume ? ButtonKind.Primary : ButtonKind.Quiet;
        pauseOrResume.IsEnabled = model.OffersResume ? !model.Busy : model.CanPause;
        rebuild.IsEnabled = turnOff.IsEnabled = model.CanReplace;
        Say(powerHint, model.PowerHint);

        Show(hold, model.HoldWords is not null);
        holdWords.Text = model.HoldWords ?? string.Empty;
        Show(holdStopping, model.HoldIsStopping);
        Show(stopHold, !model.HoldIsStopping);
        stopHold.IsEnabled = !model.Busy;

        actionProblem.Text = model.ActionProblem;
        Show(noScreen, model.NoScreenNote is not null);
        noScreenWords.Text = model.NoScreenNote ?? string.Empty;
        Show(settings, model.NoScreenLinksToSettings);
        settings.Address = Addresses.Bot(model.BotId);

        ShowTabs();
        ShowTerminal();
        ShowDialogs();
    }

    // The badge is in the row only while the bot works, so the row keeps no room for it otherwise.
    private void ShowBotWorking(bool working)
    {
        if (working && !stateRow.Children.Contains(botWorking))
        {
            stateRow.Children.Insert(1, botWorking);
        }
        else if (!working)
        {
            stateRow.Children.Remove(botWorking);
        }
    }

    private void ShowTabs()
    {
        if (!tabs.Items.Select(tab => tab.Id).SequenceEqual(model.Views.Select(tab => tab.Id)))
        {
            tabs.Items = model.Views;
        }

        if (tabs.Active != model.View)
        {
            tabs.Active = model.View;
        }

        if (screen is not null && model.Bot is { } bot)
        {
            (screen.BotId, screen.BotName, screen.Status) = (model.BotId, bot.Name, model.Status);
        }
    }

    // The picture is there only while its tab shows, so nothing is read while the terminal does.
    private object PanelFor(TabItem tab)
    {
        if (tab.Id is not BotSandboxViewModel.ScreenView)
        {
            screen = null;
            return terminal;
        }

        if (screen is null)
        {
            screen = new() { BotId = model.BotId, BotName = model.Bot?.Name ?? string.Empty, Status = model.Status };
            screen.SandboxChanged += async (_, _) => await model.RefreshStatus();
        }

        return screen;
    }

    private void ShowTerminal()
    {
        Show(openTerminal, model.OffersInteractiveTerminal);
        openTerminal.IsEnabled = !model.Busy;
        Say(terminalHint, model.Entries.Count == 0 ? model.TerminalHint : null);
        Show(history, model.Entries.Count > 0);
        ShowHistory();

        if (command.Value != model.Command)
        {
            command.Value = model.Command;
        }

        run.Content = model.RunLabel;
        run.IsEnabled = model.CanRun;
    }

    // A command keeps its row once it is done, so its output is not drawn again each time the state is read.
    private void ShowHistory()
    {
        foreach (var entry in model.Entries)
        {
            var now = entry.Problem ?? (entry.Result is null ? "running" : "done");
            if (rows.TryGetValue(entry, out var row) && row.Shown == now)
            {
                continue;
            }

            var made = Row(entry);
            if (row.Row is not null)
            {
                history.Children[history.Children.IndexOf(row.Row)] = made;
            }
            else
            {
                history.Children.Add(made);
            }

            rows[entry] = (now, made);
        }
    }

    private static StackPanel Row(SandboxCommandEntry entry)
    {
        var row = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        var words = Styles.Text(entry.Command, "DvTextCode");
        words.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(words);

        if (entry.Problem is not null)
        {
            row.Children.Add(new StatusNote { Kind = NoteKind.Danger, Text = entry.Problem });
        }
        else if (entry.Result is not { } result)
        {
            var running = Hint(live: true);
            running.Text = "Running";
            row.Children.Add(running);
        }
        else
        {
            var outcome = Hint(live: false);
            outcome.Text = SandboxWords.Outcome(result);
            row.Children.Add(outcome);
            if (result.Stdout.Length > 0)
            {
                row.Children.Add(new CodeBlock { Code = result.Stdout, CodeLanguage = "Output", CopyLabel = OutputWords.CopyOutput });
            }

            if (result.Stderr.Length > 0)
            {
                row.Children.Add(new CodeBlock { Code = result.Stderr, CodeLanguage = "Errors", CopyLabel = OutputWords.CopyOutput });
            }
        }

        return row;
    }

    private void ShowDialogs()
    {
        rebuilding.Body ??= Styles.Text(model.RebuildBody, "DvTextParagraph");
        turningOff.Body ??= Styles.Text(model.TurnOffBody, "DvTextParagraph");
        Open(rebuilding, model.Rebuilding);
        Open(stoppingHold, model.StoppingHold);
        Open(turningOff, model.TurningOff);
    }

    private static void Open(Dialog dialog, bool open)
    {
        if (dialog.Open != open)
        {
            dialog.Open = open;
        }
    }

    // The control a person just used is gone, so the heading takes the focus and is read out. It is not a tab stop
    // otherwise.
    private void FocusHeading()
    {
        headingHolder.IsTabStop = true;
        headingHolder.Focus(FocusState.Programmatic);
    }

    private static void Show(UIElement part, bool visible) => part.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static void Say(TextBlock line, string? words)
    {
        line.Text = words ?? string.Empty;
        Show(line, words is not null);
    }

    #endregion
}
