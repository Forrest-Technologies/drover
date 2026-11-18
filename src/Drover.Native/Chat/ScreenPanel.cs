namespace Drover.Native.Chat;

/// <summary>
/// A bot's sandbox screen beside the conversation, so a person can watch it and send desktop input, and hand it back
/// here when the bot asked them to take it over. The panel holds the request the person holds, every state in which
/// there is no screen to show, and the sandbox's state above the screen where there is one.
/// </summary>
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

    // The gallery's stand-in for the screen, whose own view model would ask a real server.
    private readonly UIElement? sample;

    private Drover.Native.Bots.SandboxScreen? screen;

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
    internal ScreenPanel(ScreenPanelViewModel model, UIElement? sampleScreen = null)
    {
        this.model = model;
        sample = sampleScreen;
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

    /// <summary>The person handed the screen back, or found it already answered.</summary>
    public event EventHandler<Guid>? HandedBack;

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
        surface.Content = shown ? sample ?? Screen() : null;
        if (shown)
        {
            sandbox.Text = model.SandboxState;
            sandbox.Kind = model.SandboxKind;
            working.Visibility = model.BotWorking ? Visibility.Visible : Visibility.Collapsed;
            terminal.Address = model.TerminalHref;
        }
    }

    // The screen is made when there is one to show, and kept, so the picture a person is watching is not started over
    // each time the sandbox's state is read again.
    private Drover.Native.Bots.SandboxScreen Screen()
    {
        if (screen is null)
        {
            screen = new();
            screen.SandboxChanged += async (_, _) => await model.Refresh();
        }

        (screen.BotId, screen.BotName, screen.Status, screen.Held) = (model.Bot.Id, model.Bot.Name, model.Status, model.IsHeld);
        return screen;
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
