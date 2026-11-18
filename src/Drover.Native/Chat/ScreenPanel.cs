namespace Drover.Native.Chat;

/// <summary>
/// A bot's sandbox screen beside the conversation, so a person can watch it and send desktop input, and hand it back
/// here when the bot asked them to take it over. This is the panel around the screen: the request the person holds,
/// every state in which there is no screen to show, and the sandbox's state above one that is. The screen itself is
/// what the panel is given as <see cref="Surface"/>, which reads <see cref="Status"/> and <see cref="IsHeld"/> from
/// the panel and asks it to <see cref="Refresh"/> when it changed the sandbox.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Surface))]
public sealed class ScreenPanel : Grid
{
    #region Private Fields

    private readonly Batch batch;

    private readonly ContentPresenter held = new();

    private readonly EmptyState empty = new();

    private readonly TextBlock hint = Styles.Text(null, "DvTextSecondary");

    private readonly Grid stateRow = new() { ColumnSpacing = Tokens.Number("DvSpace2") };

    private readonly Badge sandbox = new();

    private readonly Badge working = new() { Kind = BadgeKind.Live, Text = ScreenPanelViewModel.BotWorkingWords };

    private readonly Link terminal = new() { Text = ScreenPanelViewModel.TerminalText, Ink = "DvText2", HoverInk = "DvText2", MinHeight = Targets.Minimum };

    private readonly ContentPresenter surface = new();

    private ScreenPanelViewModel model;

    private TakeoverCard? card;

    private Guid? cardFor;

    private bool opened;

    private bool closed;

    #endregion

    #region Constructors

    public ScreenPanel()
        : this(Pieces.Make<ScreenPanelViewModel>())
    {
    }

    /// <summary>A panel with a view model made elsewhere, for the gallery, which reads from a sample server.</summary>
    internal ScreenPanel(ScreenPanelViewModel model)
    {
        this.model = model;
        batch = new(this, Apply, Draw);
        model.PropertyChanged += Changed;
        model.HandedBack += Returned;

        empty.Cause = ScreenPanelViewModel.FailedTitle;
        empty.ActionLabel = ScreenPanelViewModel.TryAgain;
        empty.Action += async (_, _) => await this.model.Load();

        terminal.FontSize = Tokens.Number("DvText13");
        terminal.HorizontalAlignment = HorizontalAlignment.Right;
        stateRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        stateRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        stateRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        SetColumn(working, 1);
        SetColumn(terminal, 2);
        stateRow.Children.Add(sandbox);
        stateRow.Children.Add(working);
        stateRow.Children.Add(terminal);

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace2"), Padding = new(Tokens.Number("DvSpace4")) };
        column.Children.Add(held);
        column.Children.Add(empty);
        column.Children.Add(hint);
        column.Children.Add(stateRow);
        column.Children.Add(surface);
        Children.Add(new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, IsTabStop = false });

        AutomationProperties.SetLiveSetting(hint, AutomationLiveSetting.Polite);
        Loaded += (_, _) => Open();
        Unloaded += (_, _) => Close();
        this.WhenShownOrRethemed(batch.Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<ScreenPanel, BotSummary?>(nameof(Bot), null, panel => panel.batch.Values());

    public static readonly DependencyProperty TakeoverProperty = Dp.Register<ScreenPanel, Takeover?>(nameof(Takeover), null, panel => panel.batch.Values());

    public static readonly DependencyProperty SurfaceProperty = Dp.Register<ScreenPanel, object?>(nameof(Surface), null, panel => panel.batch.Draw());

    /// <summary>The bot whose screen is shown. Required.</summary>
    public BotSummary? Bot
    {
        get => (BotSummary?)GetValue(BotProperty);
        set => SetValue(BotProperty, value);
    }

    /// <summary>The bot's request for the person at this screen, while the person holds it. Its controls are shown here.</summary>
    public Takeover? Takeover
    {
        get => (Takeover?)GetValue(TakeoverProperty);
        set => SetValue(TakeoverProperty, value);
    }

    /// <summary>
    /// The screen itself: the control that shows the sandbox's picture and sends it input. It is in the panel only
    /// while the bot has a screen to show.
    /// </summary>
    public object? Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    /// <summary>The sandbox as last read, for the surface to draw from. Null until it was read.</summary>
    public BotSandboxStatus? Status => model.Status;

    /// <summary>True while the person holds the screen for the bot's request, for the surface to say so.</summary>
    public bool IsHeld => model.IsHeld;

    /// <summary>Raised each time <see cref="Status"/> or <see cref="IsHeld"/> may have changed.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>The person handed the screen back, or found it already answered.</summary>
    public event EventHandler<Guid>? HandedBack;

    #endregion

    #region Public Methods

    /// <summary>Reads the sandbox's state again now, as the surface asks when it started, woke, or changed the sandbox.</summary>
    public Task Refresh() => model.Refresh();

    #endregion

    #region Opening And Closing

    // The sandbox's state is read when the panel opens and every ten seconds while it is open. A panel taken off the
    // screen stops reading; one put back starts again with a view model of its own.
    private async void Open()
    {
        if (opened || !IsLoaded || Bot is null)
        {
            return;
        }

        opened = true;
        if (closed)
        {
            closed = false;
            model = Pieces.Make<ScreenPanelViewModel>();
            model.PropertyChanged += Changed;
            model.HandedBack += Returned;
        }

        batch.Values();
        batch.Flush();
        await model.Open();
    }

    private async void Close()
    {
        if (!opened)
        {
            return;
        }

        (opened, closed) = (false, true);
        model.PropertyChanged -= Changed;
        model.HandedBack -= Returned;
        await model.DisposeAsync();
    }

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => batch.Draw();

    private void Returned(Guid toolCallId) => HandedBack?.Invoke(this, toolCallId);

    #endregion

    #region Drawing

    private void Apply()
    {
        if (Bot is { } bot)
        {
            (model.Bot, model.Takeover) = (bot, Takeover);
            Open();
        }
    }

    private void Draw()
    {
        if (Bot is null || model.Bot is null)
        {
            return;
        }

        DrawHeld();
        var state = model.State;

        empty.Visibility = model.EmptyTitle is null ? Visibility.Collapsed : Visibility.Visible;
        if (model.EmptyTitle is { } title)
        {
            (empty.Title, empty.Cause, empty.ActionLabel, empty.ActionHref) = (title, model.EmptyCause ?? title, model.EmptyAction ?? ScreenPanelViewModel.TryAgain, model.EmptyHref);
        }

        var said = state switch
        {
            ScreenPanelState.NotPermitted => model.NotPermittedHint,
            ScreenPanelState.Loading => ScreenPanelViewModel.Loading,
            _ => null,
        };
        hint.Visibility = said is null ? Visibility.Collapsed : Visibility.Visible;
        if (hint.Text != (said ?? string.Empty))
        {
            hint.Text = said ?? string.Empty;
            if (state is ScreenPanelState.Loading)
            {
                FrameworkElementAutomationPeer.FromElement(hint)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        var shown = state is ScreenPanelState.Screen;
        stateRow.Visibility = surface.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        surface.Content = shown ? Surface : null;
        if (shown)
        {
            sandbox.Text = model.SandboxState;
            sandbox.Kind = model.SandboxKind;
            working.Visibility = model.BotWorking ? Visibility.Visible : Visibility.Collapsed;
            terminal.Address = model.TerminalHref;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    // The request the person holds stands above the screen with its controls. Each request gets a card of its own, so
    // a half-written answer to one never shows on the next.
    private void DrawHeld()
    {
        if (model.Held is not { } asked)
        {
            (held.Content, card, cardFor) = (null, null, null);
            return;
        }

        if (card is null || cardFor != asked.ToolCallId)
        {
            card = new() { Phase = TakeoverPhase.Holding, TakeFocus = true };
            card.Done += async (_, _) => await model.HandBack(true, null);
            card.CouldNot += async (_, note) => await model.HandBack(false, note);
            (held.Content, cardFor) = (card, asked.ToolCallId);
        }

        (card.BotName, card.Reason, card.Sending, card.Problem) = (model.Bot.Name, asked.Reason, model.Handing, model.HandProblem);
    }

    #endregion
}
