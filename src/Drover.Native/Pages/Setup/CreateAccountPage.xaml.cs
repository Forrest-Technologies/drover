namespace Drover.Native.Pages;

/// <summary>First-run setup, step one: the account that owns the server.</summary>
public sealed partial class CreateAccountPage : Screen
{
    #region Constructors

    public CreateAccountPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    public override string Title => CreateAccountViewModel.Title;

    public CreateAccountViewModel Model { get; } = Make<CreateAccountViewModel>();

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) => await Model.CheckServer();

    // Enter in any field sends the form, as it does in a form on the web.
    private void Submit(object? sender, EventArgs args)
    {
        if (Model.SubmitCommand.CanExecute(null))
        {
            Model.SubmitCommand.Execute(null);
        }
    }

    #endregion
}
