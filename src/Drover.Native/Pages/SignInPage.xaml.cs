namespace Drover.Native.Pages;

/// <summary>Signing in with the account made on this server.</summary>
public sealed partial class SignInPage : Screen
{
    #region Constructors

    public SignInPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    public override string Title => SignInViewModel.Title;

    public SignInViewModel Model { get; } = Make<SignInViewModel>();

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route)
    {
        await Model.Load();
        Email.TakeFocus();
    }

    // Enter in either field sends the form, as it does in a form on the web.
    private void Submit(object? sender, EventArgs args)
    {
        if (Model.SubmitCommand.CanExecute(null))
        {
            Model.SubmitCommand.Execute(null);
        }
    }

    #endregion
}
