namespace Drover.Presentation.Platform;

/// <summary>
/// Small choices kept on the device, by name: the theme, the server address. In a browser this is the page's local
/// storage, under the names the web app always used; elsewhere it is the platform's own settings store. Never a
/// place for a secret.
/// </summary>
public interface IDeviceSettings
{
    string? Read(string name);

    void Write(string name, string value);

    void Remove(string name);
}
