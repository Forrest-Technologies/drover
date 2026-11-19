namespace Drover.Presentation.Diagnostics;

/// <summary>The diagnostic log as text files in a folder of the app's own, made when first written to.</summary>
public sealed class DiagnosticFolder(string folder) : IDiagnosticStore
{
    #region Interface Implementations

    public string? Read(string name) => File.Exists(PathOf(name)) ? File.ReadAllText(PathOf(name)) : null;

    public void Write(string name, string text)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(PathOf(name), text);
    }

    public void Append(string name, string text)
    {
        Directory.CreateDirectory(folder);
        File.AppendAllText(PathOf(name), text);
    }

    public void Remove(string name) => File.Delete(PathOf(name));

    #endregion

    #region Private Methods

    private string PathOf(string name) => Path.Combine(folder, $"{name}.txt");

    #endregion
}
