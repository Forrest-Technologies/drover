namespace Drover.Native.Pages;

/// <summary>The bots of the space. Its job: pick a bot to change, or make a new one.</summary>
public sealed partial class BotListPage : Screen
{
    #region Private Fields

    private readonly Navigator navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();

    private BotListState? shownState;

    private (IReadOnlyList<BotSummary>? Bots, bool Creating) shownCrew;

    #endregion

    #region Constructors

    public BotListPage()
    {
        InitializeComponent();
        Heading.Text = BotListViewModel.Heading;
        Loading.Text = BotListViewModel.Loading;
        NoBotForMember.Text = BotListViewModel.NoBotForMember;
        MemberHint.Text = BotListViewModel.MemberHint;
        Crew.Describe(BotListViewModel.ListName);
        Model.PropertyChanged += (_, _) => Show();
        Show();
    }

    #endregion

    #region Properties

    public override string Title => BotListViewModel.Title;

    public BotListViewModel Model { get; } = Make<BotListViewModel>();

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) => await Model.Open();

    private void Show()
    {
        var state = Model.State;
        NewBot.Shown(Model.ShowsNewBot);
        NewBot.Content = Model.NewBotLabel;
        NewBot.IsEnabled = !Model.Creating;
        ShowStage(state);
        Loading.Shown(state is BotListState.Loading);
        NoBotForMember.Shown(state is BotListState.NoBotForMember);
        MemberHint.Shown(state is BotListState.Listed && !Model.CanEdit);
        Crew.Shown(state is BotListState.Listed);

        if (shownCrew != (Model.Bots, Model.Creating))
        {
            shownCrew = (Model.Bots, Model.Creating);
            Crew.Fill(state is BotListState.Listed ? [.. Model.Crew.Select(node => new CrewRow(Model, node, Open))] : []);
        }
    }

    private void ShowStage(BotListState state)
    {
        if (state is BotListState.Failed && Stage.Content is EmptyState failed)
        {
            failed.Cause = Model.LoadProblem ?? string.Empty;
        }

        if (state is BotListState.NoBot && Stage.Content is EmptyState noBot)
        {
            noBot.ActionLabel = Model.MakeBotLabel;
        }

        if (shownState == state)
        {
            return;
        }

        shownState = state;
        Stage.Content = state switch
        {
            BotListState.Failed => Empty(BotListViewModel.LoadFailedTitle, Model.LoadProblem ?? string.Empty, BotListViewModel.TryAgain, () => _ = Model.Load()),
            BotListState.NoModel => new EmptyState
            {
                Title = BotListViewModel.NoModelTitle,
                Cause = BotListViewModel.NoModelCause,
                ActionLabel = BotListViewModel.NoModelAction,
                ActionHref = BotListViewModel.NoModelAddress,
            },
            BotListState.NoBot => Empty(BotListViewModel.NoBotTitle, BotListViewModel.NoBotCause, Model.MakeBotLabel, () => _ = Model.Create(null)),
            _ => null,
        };
        Stage.Shown(Stage.Content is not null);
    }

    private static EmptyState Empty(string title, string cause, string action, Action act)
    {
        var empty = new EmptyState { Title = title, Cause = cause, ActionLabel = action };
        empty.Action += (_, _) => act();
        return empty;
    }

    private void Open(string address) => navigator.NavigateTo(address);

    private void MakeBot(object sender, RoutedEventArgs args) => _ = Model.Create(null);

    #endregion
}
