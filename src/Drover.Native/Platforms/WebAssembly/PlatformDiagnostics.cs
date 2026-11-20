namespace Drover.Native.Platform;

/// <summary>Where a browser keeps the diagnostic log: the page's local storage (<see cref="BrowserDiagnosticStore"/>).</summary>
internal static class PlatformDiagnostics
{
    #region Properties

    public static string Kind => "Browser";

    #endregion

    #region Public Methods

    public static IDiagnosticStore Store() => new BrowserDiagnosticStore();

    #endregion
}
