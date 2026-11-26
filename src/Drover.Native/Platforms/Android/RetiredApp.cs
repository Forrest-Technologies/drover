namespace Drover.Native.Droid;

/// <summary>
/// Finds the retired Drover Preview app by its package, which the manifest names under its queries so Android lets
/// this app see it, and opens its system page, where the person can uninstall it.
/// </summary>
internal sealed class RetiredApp : IRetiredApp
{
    #region Private Fields

    private const string Package = "com.forresttechnologies.drover.preview";

    #endregion

    #region Interface Implementations

    // The retired app had a launcher entry, and Android answers null for a package that is not installed.
    public bool Installed
    {
        get
        {
            using var launch = Android.App.Application.Context.PackageManager?.GetLaunchIntentForPackage(Package);
            return launch is not null;
        }
    }

    public void OpenSettings()
    {
        var context = Platform.CurrentActivity ?? throw new InvalidOperationException("Drover has no window to open settings from.");
        using var intent = new Intent(
            Android.Provider.Settings.ActionApplicationDetailsSettings,
            Android.Net.Uri.Parse($"package:{Package}"));
        context.StartActivity(intent);
    }

    #endregion
}
