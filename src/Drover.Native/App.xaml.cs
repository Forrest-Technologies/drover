namespace Drover.Native;

public partial class App : Application
{
    #region Private Fields

    private readonly DiagnosticLog diagnostics = AppDiagnostics.Log;

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
        // reaches the log. Only a fixed crash event goes to the platform logger, never the raw exception message.
        UnhandledException += (_, failure) => diagnostics.ReportUnhandled(
            Log.For<App>(),
            LogLevel.Error,
            new EventId(1, "InterfaceUnhandled"),
            failure.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, failure) =>
        {
            if (failure.ExceptionObject is Exception exception)
            {
                diagnostics.ReportUnhandled(
                    Log.For<App>(),
                    LogLevel.Critical,
                    new EventId(2, "ProcessUnhandled"),
                    exception);
            }
        };
#if DROVER_UNREACHABLE_CHECK
        UnreachableOpenCheck.WatchUnhandled(this);
#endif

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
        services = AppServices.Build();
#if DROVER_UNREACHABLE_CHECK
        UnreachableOpenCheck.Prepare(services);
#endif
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
#if DROVER_UNREACHABLE_CHECK
            UnreachableOpenCheck.ArmFromEnvironment();
#endif
            FieldInputCheck.ArmFromEnvironment();
            PlatformShell.ReadDevice();
            MainWindow = new Window { Title = "Drover" };
            MainWindow.Activated += (_, activated) =>
                IsActive = activated.WindowActivationState is not WindowActivationState.Deactivated;
#if DROVER_TIMELINE_FOCUS_CHECK
            if (Environment.GetEnvironmentVariable("DROVER_TIMELINE_FOCUS_CHECK") == "1")
            {
                TimelineFocusCheck.Start(MainWindow);
                MainWindow.Activate();
                return;
            }
#endif
            if (FieldInputCheck.Armed)
            {
                FieldInputCheck.Start(MainWindow);
                MainWindow.Activate();
                return;
            }

            Restart();
#if DROVER_UNREACHABLE_CHECK
            UnreachableOpenCheck.Start(MainWindow);
#endif
            MainWindow.Activate();
            ActivatorUtilities.CreateInstance<AboutViewModel>(Services).OfferAfterFailure(PlatformFiles.SaveLog);
            StartChecks(MainWindow);
        }
        catch (Exception exception)
        {
            diagnostics.ReportUnhandled(
                Log.For<App>(),
                LogLevel.Critical,
                new EventId(3, "LaunchFailed"),
                exception);
            throw;
        }
    }

    // A build made for the screen checks (tests/Drover.Native.UITests) runs them here. Any other build has no body
    // for this, so the call is not compiled.
    partial void StartChecks(Window window);

    #endregion
}
