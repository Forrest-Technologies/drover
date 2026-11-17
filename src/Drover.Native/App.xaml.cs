namespace Drover.Native;

public partial class App : Application
{
    #region Constructors

    public App()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    public Window? MainWindow { get; private set; }

    #endregion

    #region Private Methods

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window { Content = new TextBlock { Text = "Drover" } };
        MainWindow.Activate();
    }

    #endregion
}
