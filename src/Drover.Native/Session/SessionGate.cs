namespace Drover.Native.Session;

/// <summary>
/// Shows what is behind it only to a signed-in person. Anyone else is sent to sign in, or to setup on a new
/// server. A server that does not answer is said in words, with a way to try again.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Body))]
public sealed class SessionGate : Grid
{
    #region Private Fields

    private readonly SessionGateViewModel model = ActivatorUtilities.CreateInstance<SessionGateViewModel>(((App)Application.Current).Services);

    private UIElement? body;

    #endregion

    #region Constructors

    public SessionGate()
    {
        model.PropertyChanged += (_, _) => Show();
        Loaded += async (_, _) => await model.Check();
        Show();
    }

    #endregion

    #region Properties

    /// <summary>What a signed-in person sees.</summary>
    public UIElement? Body
    {
        get => body;
        set
        {
            body = value;
            Show();
        }
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        Children.Clear();

        switch (model.State)
        {
            case GateState.SignedIn when body is not null:
                Children.Add(body);
                break;
            case GateState.Unreachable:
                var column = new StackPanel();
                column.Children.Add(new EmptyState
                {
                    Title = SessionGateViewModel.UnreachableTitle,
                    Cause = SessionGateViewModel.UnreachableCause,
                    ActionLabel = "Try again",
                    ActionCommand = model.CheckCommand,
                });
                column.Children.Add(new ServerChoice());
                Children.Add(Centered(column));
                break;
            default:
                var note = Styles.Text(SessionGateViewModel.CheckingNote, "DvTextSecondary");
                AutomationProperties.SetLiveSetting(note, AutomationLiveSetting.Polite);
                Children.Add(Centered(note));
                break;
        }
    }

    private static Border Centered(FrameworkElement content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        return new() { Child = content, Padding = new(Tokens.Number("DvSpace4")) };
    }

    #endregion
}
