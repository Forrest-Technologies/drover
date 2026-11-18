namespace Drover.Native.Pages;

/// <summary>First-run setup, step two: connect the model the bots think with, and test it.</summary>
public sealed partial class ConnectModelPage : Screen
{
    #region Constructors

    public ConnectModelPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    public override string Title => ConnectModelViewModel.Title;

    public ConnectModelViewModel Model { get; } = Make<ConnectModelViewModel>();

    #endregion

    #region Private Methods

    // Enter in a field tests the connection, as Enter in a form on the web sends it.
    private void Submit(object? sender, EventArgs args)
    {
        if (Model.TestCommand.CanExecute(null))
        {
            Model.TestCommand.Execute(null);
        }
    }

    #endregion
}
