namespace Drover.Presentation.Diagnostics;

/// <summary>
/// Where this device keeps its diagnostic log: a few small named texts. On a desktop or a phone they are files in the
/// app's own folder; in a browser they are entries in the page's local storage. A store that cannot be used throws
/// <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/> or <see cref="InvalidOperationException"/>.
/// </summary>
public interface IDiagnosticStore
{
    string? Read(string name);

    void Write(string name, string text);

    void Append(string name, string text);

    void Remove(string name);
}
