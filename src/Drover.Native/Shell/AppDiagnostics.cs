namespace Drover.Native.Shell;

/// <summary>The diagnostic log's place in the app: what it says the app is, and its part in the logging.</summary>
internal static class AppDiagnostics
{
    #region Public Methods

    /// <summary>This app, as a diagnostic log names it: the version it was built as, and where it runs.</summary>
    public static AppDescription ThisApp() => new(Version(), Platform());

    /// <summary>Keeps the app's warnings and failures in the diagnostic log as well as wherever else they go.</summary>
    public static ILoggingBuilder AddDiagnosticLog(this ILoggingBuilder logging, DiagnosticLog log) =>
        logging.AddProvider(new DiagnosticLogProvider(log, TimeProvider.System));

    #endregion

    #region Private Methods

    private static string Version() =>
        typeof(App).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "unknown";

    private static string Platform() =>
        $"{PlatformDiagnostics.Kind}, {RuntimeInformation.OSDescription}, {RuntimeInformation.FrameworkDescription}";

    #endregion
}
