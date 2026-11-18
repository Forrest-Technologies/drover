namespace Drover.Native.Chat;

/// <summary>
/// A run of the main conversation as a timeline: the newest run unless the person opened an earlier one from its
/// reply. It is read from the run's one stream, which replays what is stored and then follows the run live. The rail
/// holds a run's facts and the choice of form, then its timeline, which scrolls inside the rail.
/// </summary>
public sealed class ActivityPanel : Grid
{
    #region Private Fields

    private const string TimelineValue = "timeline";

    private const string ListValue = "list";

    private static readonly SegmentOption<string>[] forms =
    [
        new(TimelineValue, ActivityPanelViewModel.TimelineForm),
        new(ListValue, ActivityPanelViewModel.ListForm),
    ];

    private ActivityPanelViewModel model;

    private readonly Batch batch;

    private readonly EmptyState failed = new() { Title = ActivityPanelViewModel.FailedTitle, ActionLabel = ActivityPanelViewModel.TryAgain };

    private readonly TextBlock loading = Styles.Text(ActivityPanelViewModel.Loading, "DvTextSecondary");

    private readonly EmptyState nothing = new() { Title = ActivityPanelViewModel.NothingRanTitle };

    private readonly Grid head = new() { ColumnSpacing = Tokens.Number("DvSpace3"), RowSpacing = Tokens.Number("DvSpace2") };

    private readonly TextBlock facts = Styles.Text(null, "DvTextMono");

    private readonly SegmentedControl form = new() { Label = ActivityPanelViewModel.FormName, Options = forms, Value = TimelineValue };

    private readonly Link newest = new() { Text = ActivityPanelViewModel.NewestText, MinHeight = Targets.Minimum };

    private readonly StatusNote reconnecting = new();

    private readonly StatusNote lost = new();

    private readonly Timeline timeline = new() { EmptyText = ActivityPanelViewModel.EmptyTimeline };

    // A node's details are kept while the timeline is drawn again, so an answer on its way stays on its way.
    private readonly Dictionary<Guid, RunMomentDetail> details = [];

    private bool? headOnOneRow;

    private bool closed;

    #endregion

    #region Constructors

    public ActivityPanel()
        : this(Pieces.Make<ActivityPanelViewModel>())
    {
    }

    /// <summary>A panel with a view model made elsewhere, for the gallery, which reads from a sample server.</summary>
    internal ActivityPanel(ActivityPanelViewModel model)
    {
        this.model = model;
        batch = new(this, Apply, Draw);

        failed.Cause = ActivityPanelViewModel.FailedTitle;
        failed.Action += async (_, _) => await this.model.Load();
        (nothing.Cause, nothing.ActionLabel, nothing.ActionHref) = (ActivityPanelViewModel.NothingRanTitle, ActivityPanelViewModel.NothingRanTitle, Addresses.Home);
        Pieces.SayWhenShown(loading);

        form.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) => this.model.AsList = form.Value is ListValue);
        facts.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(facts);
        head.Children.Add(form);
        head.SizeChanged += (_, _) => FitHead();
        timeline.Detail = Detail;

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3"), Padding = new(Tokens.Number("DvSpace4")) };
        column.Children.Add(failed);
        column.Children.Add(loading);
        column.Children.Add(nothing);
        column.Children.Add(head);
        column.Children.Add(newest);
        column.Children.Add(reconnecting);
        column.Children.Add(lost);
        column.Children.Add(timeline);

        // The run is part of the working area, so it sits on the yard.
        Children.Add(new Yard());
        Children.Add(new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, IsTabStop = false });

        model.PropertyChanged += Changed;
        Loaded += (_, _) => Reopen();
        Unloaded += (_, _) => Close();
        batch.Draw();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<ActivityPanel, BotSummary?>(nameof(Bot), null, panel => panel.batch.Values());

    public static readonly DependencyProperty BotsProperty =
        Dp.Register<ActivityPanel, IReadOnlyList<BotSummary>>(nameof(Bots), [], panel => panel.batch.Values());

    public static readonly DependencyProperty RunIdProperty = Dp.Register<ActivityPanel, Guid?>(nameof(RunId), null, panel => panel.batch.Values());

    public static readonly DependencyProperty VersionProperty = Dp.Register<ActivityPanel, int>(nameof(Version), 0, panel => panel.batch.Values());

    /// <summary>The bot whose main conversation's runs are shown. Required.</summary>
    public BotSummary? Bot
    {
        get => (BotSummary?)GetValue(BotProperty);
        set => SetValue(BotProperty, value);
    }

    /// <summary>The space's bots, so each lane is named. Required.</summary>
    public IReadOnlyList<BotSummary> Bots
    {
        get => (IReadOnlyList<BotSummary>)GetValue(BotsProperty);
        set => SetValue(BotsProperty, value);
    }

    /// <summary>An earlier run to show instead of the newest.</summary>
    public Guid? RunId
    {
        get => (Guid?)GetValue(RunIdProperty);
        set => SetValue(RunIdProperty, value);
    }

    /// <summary>Changes when the bot starts or stops working, which is when the newest run may have changed.</summary>
    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    #endregion

    #region Private Methods

    // The run's stream is read off the screen's own thread of work, so what it changes is drawn from there.
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => DispatcherQueue.TryEnqueue(batch.Draw);

    // A panel taken off the screen stops following its run; one put back follows it again with a view model of its own.
    private void Reopen()
    {
        if (!closed)
        {
            return;
        }

        closed = false;
        model = Pieces.Make<ActivityPanelViewModel>();
        model.AsList = form.Value is ListValue;
        model.PropertyChanged += Changed;
        details.Clear();
        batch.Values();
    }

    private void Close()
    {
        closed = true;
        model.PropertyChanged -= Changed;
        model.Dispose();
    }

    private async void Apply()
    {
        if (Bot is not { } bot)
        {
            return;
        }

        (model.Bot, model.Bots, model.RunId, model.Version) = (bot, Bots, RunId, Version);
        await model.ParametersSet();
    }

    private void Draw()
    {
        if (Bot is null || model.Bot is null)
        {
            return;
        }

        var state = model.State;
        var run = state is ActivityPanelState.Run;

        Show(failed, state is ActivityPanelState.Failed);
        failed.Cause = model.Problem ?? ActivityPanelViewModel.FailedTitle;
        Show(loading, state is ActivityPanelState.Loading);
        Show(nothing, state is ActivityPanelState.NothingRan);
        (nothing.Cause, nothing.ActionLabel, nothing.ActionHref) = (model.NothingRanCause, model.NothingRanAction, model.NothingRanHref);
        Show(head, run);
        Show(newest, run && model.NewestHref is not null);
        Show(timeline, run);
        reconnecting.Text = run ? model.ReconnectingNote : null;
        lost.Text = run ? model.LostNote : null;

        if (!run)
        {
            return;
        }

        facts.Text = model.Facts;
        FitHead();
        newest.Address = model.NewestHref;
        timeline.Label = model.TimelineLabel;
        timeline.AsList = model.AsList;
        timeline.Model = model.Model;
        Forget();
    }

    private static void Show(UIElement element, bool shown) => element.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

    // The facts and the choice of form share a row, the choice at its end, when both fit; the choice goes under the
    // facts when they do not.
    private void FitHead()
    {
        if (head.ActualWidth <= 0)
        {
            return;
        }

        facts.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        form.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var fits = facts.DesiredSize.Width + head.ColumnSpacing + form.DesiredSize.Width <= head.ActualWidth;
        if (fits == headOnOneRow)
        {
            return;
        }

        headOnOneRow = fits;
        head.ColumnDefinitions.Clear();
        head.RowDefinitions.Clear();
        head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        head.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (fits)
        {
            head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        }
        else
        {
            head.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }

        SetColumn(form, fits ? 1 : 0);
        SetRow(form, fits ? 0 : 1);
    }

    private RunMomentDetail? Detail(TimelineNode node)
    {
        if (model.Tree is not { } tree || model.MomentOf(node) is not { } moment)
        {
            return null;
        }

        if (!details.TryGetValue(moment.Id, out var detail))
        {
            details[moment.Id] = detail = new RunMomentDetail();
        }

        (detail.Tree, detail.Moment, detail.BotName, detail.Results, detail.Decide) = (tree, moment, model.NameOf(node.Lane), model.Results, model.Decide);
        detail.ChildName = node.BranchLane is { } lane ? model.NameOf(lane) : string.Empty;
        return Pieces.Detached(detail);
    }

    // The details of a run that is no longer shown are let go.
    private void Forget()
    {
        if (model.Tree is not { } tree)
        {
            details.Clear();
            return;
        }

        foreach (var gone in details.Keys.Where(id => tree.Moments.All(moment => moment.Id != id)).ToList())
        {
            details.Remove(gone);
        }
    }

    #endregion
}
