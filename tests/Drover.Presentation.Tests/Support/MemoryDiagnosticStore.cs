namespace Drover.Presentation.Tests.Support;

/// <summary>A device's diagnostic store, kept in memory. It can be told to refuse, as a full disk or a browser with storage off does.</summary>
public sealed class MemoryDiagnosticStore : IDiagnosticStore
{
    #region Private Fields

    private readonly Lock gate = new();

    #endregion

    #region Properties

    public Dictionary<string, string> Texts { get; } = [];

    public bool Refuses { get; set; }

    /// <summary>Everything the store holds, as one text, to search for what must never be in it.</summary>
    public string All
    {
        get
        {
            lock (gate)
            {
                return string.Join('\n', Texts.Values);
            }
        }
    }

    #endregion

    #region Interface Implementations

    public string? Read(string name)
    {
        lock (gate)
        {
            return Texts.GetValueOrDefault(name);
        }
    }

    public void Write(string name, string text)
    {
        lock (gate)
        {
            Texts[Checked(name)] = text;
        }
    }

    public void Append(string name, string text)
    {
        lock (gate)
        {
            var combined = $"{Texts.GetValueOrDefault(Checked(name))}{text}";
            Texts[name] = combined.Length <= DiagnosticLog.PartLimit ? combined : combined[^DiagnosticLog.PartLimit..];
        }
    }

    public void Append(string name, string olderName, string text, int limit)
    {
        lock (gate)
        {
            var current = Texts.GetValueOrDefault(Checked(name)) ?? string.Empty;
            if (current.Length + text.Length > limit)
            {
                Texts[Checked(olderName)] = current.Length <= limit ? current : current[^limit..];
                Texts[name] = text.Length <= limit ? text : text[^limit..];
                return;
            }

            Texts[name] = $"{current}{text}";
        }
    }

    public void Remove(string name)
    {
        lock (gate)
        {
            Texts.Remove(name);
        }
    }

    #endregion

    #region Private Methods

    private string Checked(string name) => Refuses ? throw new IOException("Sample: the disk is full.") : name;

    #endregion
}
