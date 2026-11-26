namespace Drover.Native.Pages;

/// <summary>Connect and test the model provider before making the first bot.</summary>
public sealed partial class ConnectModelPage : Screen
{
    #region Constructors

    public ConnectModelPage()
    {
        InitializeComponent();
        Provider.Options = Model.Providers;
        Provider.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) => Model.ChooseProvider(Provider.Value ?? Model.Kind));
        CopyUserCode.Text = () => Model.UserCode ?? string.Empty;
        Model.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Connect a model";

    public ConnectModelViewModel Model { get; } = Make<ConnectModelViewModel>();

    #endregion

    #region Private Methods

    protected override async Task Opened(RouteMatch<Type> route)
    {
        await Model.Open();
        Provider.Options = Model.Providers;
        Refresh();
    }

    private void Refresh()
    {
        if (Provider.Options != Model.Providers)
        {
            Provider.Options = Model.Providers;
        }

        Address.Visibility = Model.IsSignIn
            ? Visibility.Collapsed
            : Model.Kind == ConnectModelViewModel.CompatibleKind ? Visibility.Visible : Visibility.Collapsed;
        Key.Visibility = Model.IsSignIn ? Visibility.Collapsed : Visibility.Visible;
        Key.Hint = Model.KeyHint;
        SignIn.Visibility = Model.IsSignIn ? Visibility.Visible : Visibility.Collapsed;
        SignInLead.Text = Model.WaitingForApproval
            ? "Enter this code on the verification page, then come back here."
            : $"Continue with {Model.SignInName} to sign in with your own plan. Usage bills that plan.";
        UserCodeRow.Visibility = string.IsNullOrEmpty(Model.UserCode) ? Visibility.Collapsed : Visibility.Visible;
        UserCode.Text = Model.UserCode ?? string.Empty;
        Verification.Address = Model.VerificationAddress;
        Verification.Visibility = string.IsNullOrEmpty(Model.VerificationAddress) ? Visibility.Collapsed : Visibility.Visible;
        Waiting.Visibility = Model.WaitingForApproval ? Visibility.Visible : Visibility.Collapsed;
        Outcome.Kind = Model.Passed ? NoteKind.Ok : NoteKind.Danger;
        Outcome.Visibility = Model.Busy || Model.WaitingForApproval ? Visibility.Collapsed : Visibility.Visible;
        Testing.Visibility = Model.Busy ? Visibility.Visible : Visibility.Collapsed;
        Continue.Visibility = Model.Passed ? Visibility.Visible : Visibility.Collapsed;
        Actions.ColumnDefinitions[1].Width = Model.Passed ? new(1, GridUnitType.Star) : GridLength.Auto;
        Actions.ColumnSpacing = Model.Passed ? Tokens.Number("DvSpace2") : 0;
        Continue.IsEnabled = Test.IsEnabled = !Model.Busy && !Model.WaitingForApproval;
        Test.Kind = Model.Passed ? ButtonKind.Quiet : ButtonKind.Primary;
        Test.Content = Model.IsSignIn
            ? Model.WaitingForApproval ? "Waiting" : Model.Passed ? "Sign in again" : $"Continue with {Model.SignInName}"
            : Model.Busy ? "Testing" : Model.Passed ? "Test again" : "Test connection";
    }

    private void Submit(object? sender, EventArgs args)
    {
        if (!Model.Busy && !Model.WaitingForApproval)
        {
            Model.TestCommand.Execute(null);
        }
    }

    #endregion
}
