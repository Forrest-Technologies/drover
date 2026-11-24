namespace Drover.Native.Pages;

/// <summary>The bots of the space. Its job: pick a bot to change, or make a new one.</summary>
public sealed partial class BotListPage : Screen
{
    #region Private Fields

    private readonly Navigator navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();

    private BotListState? shownState;

    private IReadOnlyList<BotSummary>? shownCrew;

    #endregion

    #region Constructors

    public BotListPage()
    {
        InitializeComponent();
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

    protected override Task Opened(RouteMatch<Type> route) => Model.Open();

    private void Show()
    {
        var state = Model.State;
        NewBot.Shown(Model.ShowsNewBot);
        ShowStage(state);
        Loading.Shown(state is BotListState.Loading);
        NoBotForMember.Shown(state is BotListState.NoBotForMember);
        MemberHint.Shown(state is BotListState.Listed && !Model.CanEdit);
        Crew.Shown(state is BotListState.Listed);

        if (shownCrew != Model.Bots)
        {
            shownCrew = Model.Bots;
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
            noBot.ActionLabel = BotListViewModel.MakeBotLabel;
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
            BotListState.NoBot => Empty(BotListViewModel.NoBotTitle, BotListViewModel.NoBotCause, BotListViewModel.MakeBotLabel, () => Model.Create(null)),
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

    private void MakeBot(object sender, RoutedEventArgs args) => Model.Create(null);

    #endregion
}
