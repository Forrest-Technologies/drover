namespace Drover.Native.Pages;

/// <summary>First-run setup, step three: the first bot, which leads the crew.</summary>
public sealed partial class MeetBotPage : Screen
{
    #region Constructors

    public MeetBotPage()
    {
        InitializeComponent();
        Meet.Padding = new(0, Tokens.Number("DvSpace4"), 0, Tokens.Number("DvSpace4"));
    }

    #endregion

    #region Properties

    public override string Title => MeetBotViewModel.Title;

    public MeetBotViewModel Model { get; } = Make<MeetBotViewModel>();

    #endregion

    #region Public Methods

    /// <summary>The models of the provider as the choices of the list.</summary>
    public static IReadOnlyList<SelectOption> OptionsOf(IReadOnlyList<Choice> models) =>
        [.. models.Select(entry => new SelectOption(entry.Value, entry.Label))];

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) => await Model.Start();

    // Enter in a field makes the bot, as Enter in a form on the web sends it.
    private void Submit(object? sender, EventArgs args)
    {
        if (Model.CreateCommand.CanExecute(null))
        {
            Model.CreateCommand.Execute(null);
        }
    }

    #endregion
}
