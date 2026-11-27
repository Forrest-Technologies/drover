namespace Drover.Native.Pages;

/// <summary>The bots of the space. Its job: pick a bot to change, make a new one, or bring one in from a file.</summary>
public sealed partial class BotListPage : Screen
{
    #region Private Fields

    private readonly Navigator navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();

    private readonly Sheet importSheet = new() { Title = BotListViewModel.ImportBotLabel };

    private ImportForm? importForm;

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
        importSheet.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (!importSheet.Open && Model.Importing)
            {
                Model.StopImporting();
            }
        });
        Layout.Body.Add(importSheet);
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
        ImportBot.Shown(Model.ShowsImport);
        ShowImport();
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

    // The import is made when it begins and let go when it ends: each time it starts it holds a new form.
    private void ShowImport()
    {
        if (Model.Importing && importForm is null)
        {
            importForm = new(Model, read => BotReview.Of((BotImportPreview)read));
            importSheet.Body = importForm;
        }
        else if (!Model.Importing && importForm is not null)
        {
            importForm = null;
            importSheet.Body = null;
        }

        importSheet.Open = Model.Importing;
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

    private void StartImport(object sender, RoutedEventArgs args) => Model.StartImporting();

    #endregion
}
