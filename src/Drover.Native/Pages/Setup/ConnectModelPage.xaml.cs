namespace Drover.Native.Pages;

/// <summary>Connect and test the model provider before making the first bot.</summary>
public sealed partial class ConnectModelPage : Screen
{
    #region Constructors

    public ConnectModelPage()
    {
        InitializeComponent();
        ProvidersLoading.Text = NamedProviders.LoadingWords;
        ProvidersFailedNote.Text = NamedProviders.FailedWords;
        ProvidersRetry.Content = NamedProviders.RetryWords;
        ChangeAddress.Content = NamedProviders.ChangeAddressWords;
        CopyUserCode.Text = () => Model.UserCode ?? string.Empty;
        Provider.RegisterPropertyChangedCallback(Select.ValueProperty, (_, _) =>
        {
            if (Provider.Value.Length > 0 && Provider.Value != Model.Named.Choice)
            {
                Model.ChooseProvider(Provider.Value);
            }
        });
        Model.PropertyChanged += (_, _) => Refresh();
        Unloaded += (_, _) => Model.Dispose();
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Connect a model";

    public ConnectModelViewModel Model { get; } = Make<ConnectModelViewModel>();

    #endregion

    #region Private Methods

    protected override Task Opened(RouteMatch<Type> route) => Model.Open();

    private void Refresh()
    {
        var providers = Model.Named;
        IReadOnlyList<SelectOption> options = [.. providers.Options.Select(option => new SelectOption(option.Value, option.Label))];
        if (!Provider.Options.SequenceEqual(options))
        {
            Provider.Options = options;
        }

        if (Provider.Value != providers.Choice)
        {
            Provider.Value = providers.Choice;
        }

        ProvidersLoading.Visibility = providers.ShowsLoading ? Visibility.Visible : Visibility.Collapsed;
        ProvidersFailed.Visibility = providers.ShowsFailure ? Visibility.Visible : Visibility.Collapsed;
        Reach.Text = providers.ReachNote ?? string.Empty;
        Reach.Visibility = providers.ReachNote is null ? Visibility.Collapsed : Visibility.Visible;
        Address.Visibility = Model.IsSignIn ? Visibility.Collapsed : providers.ShowsAddress ? Visibility.Visible : Visibility.Collapsed;
        ChangeAddress.Visibility = Model.IsSignIn ? Visibility.Collapsed : providers.OffersAddressChange ? Visibility.Visible : Visibility.Collapsed;
        Key.Label = providers.KeyLabel;
        Key.Hint = providers.KeyHint;
        Key.Visibility = Model.IsSignIn ? Visibility.Collapsed : providers.ShowsKey ? Visibility.Visible : Visibility.Collapsed;
        KeyLink.Text = providers.KeyLinkText ?? string.Empty;
        KeyLink.Address = providers.KeyLinkAddress;
        KeyLink.Visibility = Model.IsSignIn || providers.KeyLinkText is null || !providers.ShowsKey
            ? Visibility.Collapsed
            : Visibility.Visible;
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
