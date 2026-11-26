namespace Drover.Native.Pages;

/// <summary>Connect and test the model provider before making the first bot.</summary>
public sealed partial class ConnectModelPage : Screen
{
    #region Constructors

    public ConnectModelPage()
    {
        InitializeComponent();
        Provider.Options = ConnectModelViewModel.Providers;
        Provider.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) => Model.ChooseProvider(Provider.Value ?? Model.Kind));
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
        Address.Visibility = Model.Kind == ConnectModelViewModel.CompatibleKind ? Visibility.Visible : Visibility.Collapsed;
        Key.Hint = Model.KeyHint;
        Outcome.Kind = Model.Passed ? NoteKind.Ok : NoteKind.Danger;
        Outcome.Visibility = Model.Busy ? Visibility.Collapsed : Visibility.Visible;
        Testing.Visibility = Model.Busy ? Visibility.Visible : Visibility.Collapsed;
        Continue.Visibility = Model.Passed ? Visibility.Visible : Visibility.Collapsed;
        Actions.ColumnDefinitions[1].Width = Model.Passed ? new(1, GridUnitType.Star) : GridLength.Auto;
        Actions.ColumnSpacing = Model.Passed ? Tokens.Number("DvSpace2") : 0;
        Continue.IsEnabled = Test.IsEnabled = !Model.Busy;
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
