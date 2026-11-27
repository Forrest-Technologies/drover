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
        SignInNoteHolder.LostFocus += (_, _) => SignInNoteHolder.IsTabStop = false;
        ProviderSegments.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) =>
        {
            if (ProviderSegments.Value is { Length: > 0 } chosen && chosen != Model.Named.Choice)
            {
                Model.ChooseProvider(chosen);
            }
        });
        Model.PropertyChanged += (_, _) => Refresh();
        SizeChanged += (_, _) => Refresh();
        Form.SizeChanged += (_, _) => Refresh();
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

        var list = providers.UsesList || (providers.ShowsChoice && Form.ActualWidth > 0 && ProviderSegments.MinimumWidth > Form.ActualWidth);
        Provider.Visibility = providers.ShowsChoice && list ? Visibility.Visible : Visibility.Collapsed;
        ProviderSegments.Visibility = providers.ShowsChoice && !list ? Visibility.Visible : Visibility.Collapsed;
        OnlyKind.Text = providers.OnlyKindWords ?? string.Empty;
        OnlyKind.Visibility = providers.OnlyKindWords is null ? Visibility.Collapsed : Visibility.Visible;
        Address.Hint = providers.AddressHint;
        Address.Error = Model.AddressProblem;
        Key.Error = Model.KeyProblem;

        ProvidersLoading.Visibility = providers.ShowsLoading ? Visibility.Visible : Visibility.Collapsed;
        ProvidersFailed.Title = providers.StateTitle;
        ProvidersFailed.Cause = providers.StateCause;
        ProvidersFailed.Visibility = providers.ShowsState ? Visibility.Visible : Visibility.Collapsed;
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
        Outcome.Visibility = Model.Busy || Model.ShowsSignIn ? Visibility.Collapsed : Visibility.Visible;
        Testing.Visibility = Model.Busy ? Visibility.Visible : Visibility.Collapsed;
        Continue.Visibility = Visibility.Visible;
        ArrangeActions();
        RefreshSignIn();
        Continue.IsEnabled = Test.IsEnabled = !Model.Busy;
        Test.Content = Model.Busy ? "Testing" : Model.Passed ? "Test again" : "Test connection";
    }

    /// <summary>
    /// A sign-in kind is one line, one sentence for where it stands, and one button: no key, no address, no test.
    /// </summary>
    private void RefreshSignIn()
    {
        var shown = Model.ShowsSignIn;
        var flow = Model.SignIn;
        SignInPanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        Test.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;

        if (!shown)
        {
            return;
        }

        // Continue moves on once the person is signed in; there is nothing to save untested.
        Continue.Visibility = Model.Passed ? Visibility.Visible : Visibility.Collapsed;

        Key.Visibility = Address.Visibility = KeyLink.Visibility = ChangeAddress.Visibility = Visibility.Collapsed;
        PlanLine.Text = flow.PlanLine;
        PlanLine.Visibility = flow.Stage is ModelSignInStage.Ready ? Visibility.Visible : Visibility.Collapsed;
        var changed = flow.Sentence is { Length: > 0 } && SignInNote.Text != flow.Sentence;
        SignInNote.Text = flow.Sentence ?? string.Empty;
        SignInNote.Kind = flow.Stage switch
        {
            ModelSignInStage.Done => NoteKind.Ok,
            ModelSignInStage.Waiting => NoteKind.Info,
            _ => NoteKind.Danger,
        };
        SignInNote.Visibility = flow.Sentence is null ? Visibility.Collapsed : Visibility.Visible;
        SignInAction.Content = flow.ActionWords;
        AutomationProperties.SetName(SignInAction, flow.ActionWords ?? string.Empty);
        SignInAction.Visibility = flow.ActionWords is null ? Visibility.Collapsed : Visibility.Visible;
        SignInAction.Kind = flow.Action is ModelSignInAction.Continue or ModelSignInAction.TryAgain ? ButtonKind.Primary : ButtonKind.Quiet;

        // Focus moves to the sentence when the state changes, so a keyboard or screen reader lands on it.
        if (changed)
        {
            SignInNoteHolder.IsTabStop = true;
            SignInNoteHolder.Focus(FocusState.Programmatic);
        }
    }

    // The flow says in its own sentence when the page could not be opened.
    private void ActOnSignIn(object sender, RoutedEventArgs args) => _ = Model.ActOnSignIn(PlatformShell.OpenOutside);

    private bool IsPhone() => XamlRoot is { Size.Width: > 0 } root && root.Size.Width < (double)Styles.Dictionary["DvBreakPhone"];

    // Test connection is Quiet and Continue is Primary, side by side with a gap. Below the phone width the two stack,
    // Continue first.
    private void ArrangeActions()
    {
        var phone = IsPhone();
        Actions.ColumnSpacing = phone ? 0 : Tokens.Number("DvSpace2");
        Actions.RowSpacing = phone ? Tokens.Number("DvSpace2") : 0;
        Actions.ColumnDefinitions[1].Width = phone ? new(0) : new(1, GridUnitType.Star);
        Grid.SetRow(Continue, 0);
        Grid.SetColumn(Continue, phone ? 0 : 1);
        Grid.SetRow(Test, phone ? 1 : 0);
        Grid.SetColumn(Test, 0);
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
