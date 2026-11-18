namespace Drover.Native.Pages;

/// <summary>The model connections of the space: see whether each works, add one, test it, replace its key.</summary>
public sealed partial class ModelConnectionsPage : Screen
{
    #region Constructors

    public ModelConnectionsPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    public override string Title => ModelConnectionsViewModel.PageTitle;

    public ModelConnectionsViewModel Model { get; } = Make<ModelConnectionsViewModel>();

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) => await Model.Start();

    // Enter in a field of the form adds the connection, as Enter in a form on the web sends it.
    private void SubmitAdd(object? sender, EventArgs args)
    {
        if (Model.AddCommand.CanExecute(null))
        {
            Model.AddCommand.Execute(null);
        }
    }

    #endregion
}
