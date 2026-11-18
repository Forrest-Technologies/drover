namespace Drover.Native.Pages;

/// <summary>
/// The tool connections of the space: add a tool server, see whether it is connected, sign in to it, review changed
/// tools, remove it. The address can carry how a sign-in at the provider ended, or the ticket of one that ended in a
/// browser outside the app.
/// </summary>
public sealed partial class ToolConnectionsPage : Screen
{
    #region Constructors

    public ToolConnectionsPage()
    {
        InitializeComponent();
        Model.ProviderPageRequested += ProviderPage.Open;
    }

    #endregion

    #region Properties

    public override string Title => ToolConnectionsViewModel.PageTitle;

    public ToolConnectionsViewModel Model { get; } = Make<ToolConnectionsViewModel>();

    #endregion

    #region Public Methods

    /// <summary>The choices of a list as the options of a select.</summary>
    public static IReadOnlyList<SelectOption> OptionsOf(IReadOnlyList<Choice> choices) =>
        [.. choices.Select(choice => new SelectOption(choice.Value, choice.Label))];

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) =>
        await Model.Open(
            route.QueryValue("sign-in"),
            Guid.TryParse(route.QueryValue("connection"), out var connection) ? connection : null,
            route.QueryValue("ticket"));

    // Enter in a field of the form adds the server, as Enter in a form on the web sends it.
    private void SubmitAdd(object? sender, EventArgs args)
    {
        if (Model.Form?.AddCommand.CanExecute(null) is true)
        {
            Model.Form.AddCommand.Execute(null);
        }
    }

    #endregion
}
