namespace Drover.Native.Pages;

/// <summary>Create the first account on this server, proving access to the setup code when it is required.</summary>
public sealed partial class CreateAccountPage : Screen
{
    #region Constructors

    public CreateAccountPage()
    {
        InitializeComponent();
        Model.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Set up Drover";

    public CreateAccountViewModel Model { get; } = Make<CreateAccountViewModel>();

    #endregion

    #region Private Methods

    protected override Task Opened(RouteMatch<Type> route) => Model.Open();

    private void Refresh()
    {
        Checking.Visibility = Model.Status is null ? Visibility.Visible : Visibility.Collapsed;
        CheckingText.Visibility = Model.Problem is null ? Visibility.Visible : Visibility.Collapsed;
        Retry.Visibility = Model.Problem is null ? Visibility.Collapsed : Visibility.Visible;
        Form.Visibility = Model.Status is null ? Visibility.Collapsed : Visibility.Visible;
        Code.Visibility = Model.Status is { RequiresCode: true } ? Visibility.Visible : Visibility.Collapsed;
        Create.IsEnabled = !Model.Busy;
        Create.Content = Model.Busy ? "Creating your account" : "Create account";
    }

    private void Submit(object? sender, EventArgs args)
    {
        if (!Model.Busy && Model.Status is not null)
        {
            Model.SubmitCommand.Execute(null);
        }
    }

    #endregion
}
