namespace Drover.Presentation.Tests.Support;

/// <summary>A device's settings store, kept in memory. It can be told to refuse, as a browser with storage off does.</summary>
public sealed class MemorySettings : IDeviceSettings
{
    #region Properties

    public Dictionary<string, string> Values { get; } = [];

    public bool Refuses { get; set; }

    #endregion

    #region Interface Implementations

    public string? Read(string name) => Refuses ? throw new InvalidOperationException("Sample: storage is off.") : Values.GetValueOrDefault(name);

    public void Write(string name, string value)
    {
        if (Refuses)
        {
            throw new InvalidOperationException("Sample: storage is off.");
        }

        Values[name] = value;
    }

    public void Remove(string name) => Values.Remove(name);

    #endregion
}
