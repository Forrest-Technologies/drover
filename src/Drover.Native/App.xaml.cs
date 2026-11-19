namespace Drover.Native;

public partial class App : Application
{
    #region Private Fields

    private readonly DiagnosticLog diagnostics = new(PlatformDiagnostics.Store(), TimeProvider.System);

    private readonly AppDescription description = AppDiagnostics.ThisApp();

    private ServiceProvider? services;

    #endregion

    #region Constructors

    public App()
    {
        // Until the services exist, a failure to start still has somewhere to be written.
        Log.Factory = LoggerFactory.Create(logging => logging.AddPlatformLog().AddDiagnosticLog(diagnostics));
        Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = Log.Factory;
        Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
        // Each failure is kept apart first, so the next start can offer it even if the app stops before the line
        // reaches the log.
        UnhandledException += (_, failure) =>
        {
            diagnostics.RecordFailure(failure.Exception);
            Log.For<App>().LogError(failure.Exception, "Something in the interface failed and was not handled");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, failure) =>
        {
            if (failure.ExceptionObject is Exception exception)
            {
                diagnostics.RecordFailure(exception);
                Log.For<App>().LogCritical(exception, "Something failed that nothing handled, and the app stops");
            }
        };

        InitializeComponent();
        Resources.MergedDictionaries.Add(Tokens.Dictionary);
        Resources.MergedDictionaries.Add(Styles.Dictionary);

        // Every piece of text is Inter unless it says otherwise, in a popup as on a page.
        Uno.UI.FeatureConfiguration.Font.DefaultTextFontFamily = "ms-appx:///Assets/Fonts/Inter.ttf";
    }

    #endregion

    #region Properties

    public Window? MainWindow { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The services of the app. A screen makes its view model from them; nothing else reaches for them.</summary>
    public IServiceProvider Services => services ?? throw new InvalidOperationException("The app has not started yet.");

    #endregion

    #region Public Methods

    /// <summary>
    /// Starts the interface again with nothing kept: new services, a new shell, the first address. Used when the
    /// person points the app at another server.
    /// </summary>
    public void Restart()
    {
        var before = services;
        services = AppServices.Build(diagnostics, description);
        before?.Dispose();

        var window = MainWindow ?? throw new InvalidOperationException("The app has no window yet.");
        var navigator = services.GetRequiredService<Navigator>();
        // From here the platform's own warnings and errors go to the same log as the app's.
        Log.Factory = services.GetRequiredService<ILoggerFactory>();
        Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = Log.Factory;
        Link.Opener = address => navigator.NavigateTo(address);
        DialogSurface.Navigator = navigator;
        PlatformShell.Start(navigator);
        services.GetRequiredService<ILiveNotifications>().Start();

        var shell = new ShellView(services, window);
        window.Content = shell;
        shell.Open();
    }

    #endregion

    #region Private Methods

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            PlatformShell.ReadDevice();
            MainWindow = new Window { Title = "Drover" };
            MainWindow.Activated += (_, activated) =>
                IsActive = activated.WindowActivationState is not WindowActivationState.Deactivated;
            Restart();
            MainWindow.Activate();
            ActivatorUtilities.CreateInstance<AboutViewModel>(Services).OfferAfterFailure(PlatformFiles.SaveLog);
        }
        catch (Exception exception)
        {
            diagnostics.RecordFailure(exception);
            Log.For<App>().LogCritical(exception, "The app could not start");
            throw;
        }
    }

    #endregion
}
