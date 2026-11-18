namespace Drover.Native.Pages;

/// <summary>
/// What the server and its parts are doing, for the people who run the space: whether this server and the space's
/// connections are working, and what to do about the ones that are not.
/// </summary>
public sealed partial class StatusPage : Screen
{
    #region Constructors

    public StatusPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    public override string Title => StatusViewModel.Title;

    public StatusViewModel Model { get; } = Make<StatusViewModel>();

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) => await Model.Start();

    #endregion
}
