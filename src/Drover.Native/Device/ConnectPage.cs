namespace Drover.Native.Platform;

/// <summary>The first screen on a device: which server the app talks to.</summary>
public sealed class ConnectPage : Screen
{
    #region Private Fields

    private readonly ConnectViewModel model = Make<ConnectViewModel>();

    private readonly Field address = new() { Label = "Server address", Type = "url", Placeholder = "https://drover.example.org" };

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly Button connect = new() { Kind = ButtonKind.Primary, HorizontalAlignment = HorizontalAlignment.Left };

    #endregion

    #region Constructors

    public ConnectPage()
    {
        var heading = Styles.Text("Connect to Drover", "DvTextH2");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        var form = new StackPanel { Spacing = Tokens.Number("DvSpace4") };
        form.Children.Add(address);
        form.Children.Add(problem);
        form.Children.Add(connect);

        var layout = new EntryLayout();
        layout.Body.Add(heading);
        layout.Body.Add(Styles.Text("Enter the HTTPS address of your Drover server.", "DvTextSecondary"));
        layout.Body.Add(form);
        Content = layout;

        AutomationProperties.SetAutomationId(address, "ConnectAddress");
        AutomationProperties.SetAutomationId(connect, "ConnectSubmit");
        AutomationProperties.SetAutomationId(problem, "ConnectProblem");
        connect.Command = model.ConnectCommand;
        address.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) => model.Address = address.Value);
        address.Submitted += (_, _) => model.ConnectCommand.Execute(null);
        model.PropertyChanged += (_, _) => Show();
        Loaded += (_, _) => address.TakeFocus();
        Show();
    }

    #endregion

    #region Properties

    public override string Title => ConnectViewModel.Title;

    #endregion

    #region Private Methods

    private void Show()
    {
        address.Disabled = model.Busy;
        problem.Text = model.Problem;
        connect.Content = model.SubmitLabel;
    }

    #endregion
}
