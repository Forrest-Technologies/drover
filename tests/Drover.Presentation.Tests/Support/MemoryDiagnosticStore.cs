namespace Drover.Presentation.Tests.Support;

/// <summary>A device's diagnostic store, kept in memory. It can be told to refuse, as a full disk or a browser with storage off does.</summary>
public sealed class MemoryDiagnosticStore : IDiagnosticStore
{
    #region Properties

    public Dictionary<string, string> Texts { get; } = [];

    public bool Refuses { get; set; }

    /// <summary>Everything the store holds, as one text, to search for what must never be in it.</summary>
    public string All => string.Join('\n', Texts.Values);

    #endregion

    #region Interface Implementations

    public string? Read(string name) => Texts.GetValueOrDefault(name);

    public void Write(string name, string text) => Texts[Checked(name)] = text;

    public void Append(string name, string text) => Texts[Checked(name)] = $"{Texts.GetValueOrDefault(name)}{text}";

    public void Remove(string name) => Texts.Remove(name);

    #endregion

    #region Private Methods

    private string Checked(string name) => Refuses ? throw new IOException("Sample: the disk is full.") : name;

    #endregion
}
