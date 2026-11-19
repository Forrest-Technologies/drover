namespace Drover.Presentation.Diagnostics;

/// <summary>The diagnostic log as text files in a folder of the app's own, made when first written to.</summary>
public sealed class DiagnosticFolder(string folder) : IDiagnosticStore
{
    #region Private Fields

    private static readonly ConcurrentDictionary<string, Lock> folderLocks = new(StringComparer.Ordinal);

    private readonly Lock gate = folderLocks.GetOrAdd(Path.GetFullPath(folder), static _ => new Lock());

    #endregion

    #region Interface Implementations

    public string? Read(string name)
    {
        lock (gate)
        {
            return File.Exists(PathOf(name)) ? File.ReadAllText(PathOf(name)) : null;
        }
    }

    public void Write(string name, string text)
    {
        lock (gate)
        {
            Directory.CreateDirectory(folder);
            var target = PathOf(name);
            var temp = Path.Combine(folder, $"{name}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temp, text);
            File.Move(temp, target, overwrite: true);
        }
    }

    public void Append(string name, string text)
    {
        lock (gate)
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(PathOf(name), text);
        }
    }

    public void Remove(string name)
    {
        lock (gate)
        {
            File.Delete(PathOf(name));
        }
    }

    #endregion

    #region Private Methods

    private string PathOf(string name) => Path.Combine(folder, $"{name}.txt");

    #endregion
}
