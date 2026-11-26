namespace Drover.Ui.Session;

/// <summary>
/// The retired Android app, Drover Preview, which a person who updated in place may still have installed beside this
/// one. It had its own package, so installing this app never replaced it, and it no longer updates. Only Android has
/// one.
/// </summary>
public interface IRetiredApp
{
    /// <summary>The name the retired app shows under its icon.</summary>
    public const string Name = "Drover Preview";

    /// <summary>Whether the retired app is still on this device.</summary>
    bool Installed { get; }

    /// <summary>Opens the system's page for the retired app, where the person can uninstall it.</summary>
    void OpenSettings();
}
