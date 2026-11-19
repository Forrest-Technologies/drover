#if DROVER_BROWSER
namespace Drover.Native.Checks;

/// <summary>
/// The page the browser harness opened. It installs <c>droverUiCheckRead</c> and <c>droverUiCheckWrite</c> before
/// the app starts: one reads a setting, the other records a line the harness polls. A direct call to the page's
/// storage function can throw, because the browser refuses a function taken off the storage object.
/// </summary>
internal static partial class PageCheckChannel
{
    #region Public Methods

    /// <summary>The stored setting, or null when the page has none or refuses storage.</summary>
    public static string? Read(string name)
    {
        try
        {
#pragma warning disable CA1416
            return ReadStored(name);
#pragma warning restore CA1416
        }
        catch (JSException exception)
        {
            Log.Factory.CreateLogger(nameof(PageCheckChannel))
                .LogWarning(exception, "The browser checks could not read their settings.");
            return null;
        }
    }

    /// <summary>Records one report line on the page, where the harness reads it.</summary>
    public static void Write(string line)
    {
        try
        {
#pragma warning disable CA1416
            WriteToPage(line);
#pragma warning restore CA1416
        }
        catch (JSException exception)
        {
            Log.Factory.CreateLogger(nameof(PageCheckChannel))
                .LogWarning(exception, "The browser checks could not report to the page.");
        }
    }

    #endregion

    #region Private Methods

#pragma warning disable CA1416
    [JSImport("globalThis.droverUiCheckRead")]
    private static partial string? ReadStored(string name);

    [JSImport("globalThis.droverUiCheckWrite")]
    private static partial void WriteToPage(string line);
#pragma warning restore CA1416

    #endregion
}
#endif
