namespace Drover.Native.Pages;

/// <summary>Connect and test the model provider before making the first bot.</summary>
public sealed partial class ConnectModelPage : Screen
{
    #region Constructors

    public ConnectModelPage()
    {
        InitializeComponent();
        ProvidersLoading.Text = NamedProviders.LoadingWords;
        ProvidersFailed.Title = NamedProviders.FailedTitle;
        ProvidersFailed.ActionLabel = NamedProviders.RetryWords;
        ProvidersFailed.Cause = NamedProviders.FailedCause;
        ChangeAddress.Content = NamedProviders.ChangeAddressWords;
        Provider.RegisterPropertyChangedCallback(Select.ValueProperty, (_, _) =>
        {
            if (Provider.Value.Length > 0 && Provider.Value != Model.Named.Choice)
            {
                Model.ChooseProvider(Provider.Value);
            }
        });
        ProviderSegments.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) =>
        {
            if (ProviderSegments.Value is { Length: > 0 } chosen && chosen != Model.Named.Choice)
            {
                Model.ChooseProvider(chosen);
            }
        });
        Model.PropertyChanged += (_, _) => Refresh();
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

        if (!ProviderSegments.Options.SequenceEqual(providers.Options))
        {
            ProviderSegments.Options = providers.Options;
        }

        if (ProviderSegments.Value != providers.Choice)
        {
            ProviderSegments.Value = providers.Choice;
        }

        Provider.Visibility = providers.ShowsChoice && providers.UsesList ? Visibility.Visible : Visibility.Collapsed;
        ProviderSegments.Visibility = providers.ShowsChoice && !providers.UsesList ? Visibility.Visible : Visibility.Collapsed;
        OnlyKind.Text = providers.OnlyKindWords ?? string.Empty;
        OnlyKind.Visibility = providers.OnlyKindWords is null ? Visibility.Collapsed : Visibility.Visible;
        Address.Hint = providers.AddressHint;
        Address.Error = Model.AddressProblem;
        Key.Error = Model.KeyProblem;

        ProvidersLoading.Visibility = providers.ShowsLoading ? Visibility.Visible : Visibility.Collapsed;
        ProvidersFailed.Cause = providers.FailureWords ?? NamedProviders.FailedCause;
        ProvidersFailed.Visibility = providers.ShowsFailure ? Visibility.Visible : Visibility.Collapsed;
        Form.Visibility = providers.ShowsForm ? Visibility.Visible : Visibility.Collapsed;
        Reach.Text = providers.ReachNote ?? string.Empty;
        Reach.Visibility = providers.ReachNote is null ? Visibility.Collapsed : Visibility.Visible;
        Address.Visibility = providers.ShowsAddress ? Visibility.Visible : Visibility.Collapsed;
        ChangeAddress.Visibility = providers.OffersAddressChange ? Visibility.Visible : Visibility.Collapsed;
        Key.Label = providers.KeyLabel;
        Key.Hint = providers.KeyHint;
        Key.Visibility = providers.ShowsKey ? Visibility.Visible : Visibility.Collapsed;
        KeyLink.Text = providers.KeyLinkText ?? string.Empty;
        KeyLink.Address = providers.KeyLinkAddress;
        KeyLink.Visibility = providers.KeyLinkText is null || !providers.ShowsKey ? Visibility.Collapsed : Visibility.Visible;
        Outcome.Kind = Model.Passed ? NoteKind.Ok : NoteKind.Danger;
        Outcome.Visibility = Model.Busy ? Visibility.Collapsed : Visibility.Visible;
        Testing.Visibility = Model.Busy ? Visibility.Visible : Visibility.Collapsed;
        Continue.Visibility = Visibility.Visible;
        Actions.ColumnDefinitions[1].Width = Model.Passed ? new(1, GridUnitType.Star) : GridLength.Auto;
        Actions.ColumnSpacing = Model.Passed ? Tokens.Number("DvSpace2") : 0;
        Continue.IsEnabled = Test.IsEnabled = !Model.Busy;
        Continue.Kind = Model.Passed ? ButtonKind.Primary : ButtonKind.Quiet;
        Test.Kind = Model.Passed ? ButtonKind.Quiet : ButtonKind.Primary;
        Test.Content = Model.Busy ? "Testing" : Model.Passed ? "Test again" : "Test connection";
    }

    private void Submit(object? sender, EventArgs args)
    {
        if (!Model.Busy)
        {
            Model.TestCommand.Execute(null);
        }
    }

    #endregion
}
