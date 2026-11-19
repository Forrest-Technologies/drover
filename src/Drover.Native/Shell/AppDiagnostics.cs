namespace Drover.Native.Shell;

/// <summary>The diagnostic log's place in the app: what it says the app is, and its part in the logging.</summary>
internal static class AppDiagnostics
{
    #region Private Fields

    private static readonly IReadOnlySet<string> unhandledFrom =
        new HashSet<string>(["Uno.UI.Dispatching.NativeDispatcher"], StringComparer.Ordinal);

    #endregion

    #region Properties

    /// <summary>
    /// The one diagnostic log of the process. It exists before the interface's services do, so a failure to start is
    /// kept, and services made with no interface (a notification arriving on Android) write to the same one.
    /// </summary>
    public static DiagnosticLog Log { get; } = new(PlatformDiagnostics.Store(), TimeProvider.System);

    /// <summary>This app, as a diagnostic log names it: the version it was built as, and where it runs.</summary>
    public static AppDescription ThisApp { get; } = new(Version(), Platform());

    #endregion

    #region Public Methods

    /// <summary>
    /// Keeps the app's warnings and failures in the diagnostic log as well as wherever else they go. The platform's
    /// dispatcher catches a failure nothing handled in work it runs for the interface and only logs it, so its
    /// errors count as such failures.
    /// </summary>
    public static ILoggingBuilder AddDiagnosticLog(this ILoggingBuilder logging, DiagnosticLog log) =>
        logging.AddProvider(new DiagnosticLogProvider(log, TimeProvider.System, unhandledFrom));

    #endregion

    #region Private Methods

    // A development build can carry no informational version, so the assembly's own version stands in.
    private static string Version() =>
        typeof(App).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion
        ?? typeof(App).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static string Platform()
    {
        var system = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        var runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        return system == PlatformDiagnostics.Kind
            ? $"{PlatformDiagnostics.Kind}, {runtime}"
            : $"{PlatformDiagnostics.Kind}, {system}, {runtime}";
    }

    #endregion
}
