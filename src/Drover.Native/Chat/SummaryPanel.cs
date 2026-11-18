namespace Drover.Native.Chat;

/// <summary>The summary the server keeps of a bot's main conversation, read as it stands now.</summary>
public sealed class SummaryPanel : Grid
{
    #region Private Fields

    private readonly SummaryPanelViewModel model;

    private readonly Batch batch;

    private readonly EmptyState failed = new() { Title = SummaryPanelViewModel.FailedTitle, ActionLabel = SummaryPanelViewModel.TryAgain };

    private readonly TextBlock hint = Styles.Text(null, "DvTextSecondary");

    private readonly TextBlock facts = Styles.Text(null, "DvTextMono");

    private readonly StatusNote stays = new();

    private readonly MarkdownView text = new();

    #endregion

    #region Constructors

    public SummaryPanel()
        : this(Pieces.Make<SummaryPanelViewModel>())
    {
    }

    /// <summary>A panel with a view model made elsewhere, for the gallery, which reads from a sample server.</summary>
    internal SummaryPanel(SummaryPanelViewModel model)
    {
        this.model = model;
        batch = new(this, Apply, Draw);
        failed.Cause = SummaryPanelViewModel.FailedTitle;
        failed.Action += async (_, _) => await model.Load();
        AutomationProperties.SetLiveSetting(hint, AutomationLiveSetting.Polite);

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3"), Padding = new(Tokens.Number("DvSpace4")) };
        column.Children.Add(failed);
        column.Children.Add(hint);
        column.Children.Add(facts);
        column.Children.Add(stays);
        column.Children.Add(text);
        Children.Add(new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, IsTabStop = false });

        model.PropertyChanged += (_, _) => batch.Draw();
        batch.Draw();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<SummaryPanel, BotSummary?>(nameof(Bot), null, panel => panel.batch.Values());

    public static readonly DependencyProperty VersionProperty = Dp.Register<SummaryPanel, int>(nameof(Version), 0, panel => panel.batch.Values());

    /// <summary>The bot whose main conversation is summarized. Required.</summary>
    public BotSummary? Bot
    {
        get => (BotSummary?)GetValue(BotProperty);
        set => SetValue(BotProperty, value);
    }

    /// <summary>Changes when the conversation has moved on, which is when the summary may have been rewritten.</summary>
    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    #endregion

    #region Private Methods

    private async void Apply()
    {
        if (Bot is not { } bot)
        {
            return;
        }

        (model.Bot, model.Version) = (bot, Version);
        await model.ParametersSet();
    }

    private void Draw()
    {
        var state = model.State;
        var ready = state is SummaryPanelState.Ready;

        failed.Visibility = state is SummaryPanelState.Failed ? Visibility.Visible : Visibility.Collapsed;
        failed.Cause = model.Problem ?? SummaryPanelViewModel.FailedTitle;

        var said = model.Hint;
        hint.Visibility = said is null ? Visibility.Collapsed : Visibility.Visible;
        if (hint.Text != (said ?? string.Empty))
        {
            hint.Text = said ?? string.Empty;

            // Only "loading" is a status: the other sentences are what the panel holds.
            if (state is SummaryPanelState.Loading)
            {
                FrameworkElementAutomationPeer.FromElement(hint)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        facts.Visibility = text.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        stays.Text = model.StaysNote;
        if (ready)
        {
            facts.Text = model.Facts;
            text.Text = model.Text ?? string.Empty;
        }
    }

    #endregion
}
