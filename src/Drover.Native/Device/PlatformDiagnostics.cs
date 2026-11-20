namespace Drover.Native.Platform;

/// <summary>Where a device keeps its diagnostic log: text files in a folder of the app's own.</summary>
internal static class PlatformDiagnostics
{
    #region Properties

    public static string Kind => OperatingSystem.IsAndroid() ? "Android" : "Desktop";

    #endregion

    #region Public Methods

    public static IDiagnosticStore Store() =>
        new DiagnosticFolder(System.IO.Path.Combine(AppDataFolder, "Drover", "diagnostics"));

    #endregion

    #region Private Methods

    private static string AppDataFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);

    #endregion
}
