namespace Drover.Domain.Extensions;

/// <summary>
/// An extension a space has imported: which one, the file it came from, and what a person has set up from it. What
/// the extension offers (its themes, its rules) is read from the file each time, so removing the row takes it all
/// away. A space holds an extension id at most once.
/// </summary>
public sealed class ExtensionInstall : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    /// <summary>The id the file gives itself, the same wherever it is installed.</summary>
    public required string PackageId { get; set; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>The SHA-256 of the file's text as it was imported, in hex.</summary>
    public required string ContentHash { get; set; }

    /// <summary>The file as it was imported.</summary>
    public JsonElement Package { get; set; }

    /// <summary>The values a person gave the extension's settings, by key. A secret setting is never kept here.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>The tool connection made from the file's recipe. Null until a person connects it, and after it is removed.</summary>
    public Guid? ToolConnectionId { get; set; }

    public Guid InstalledByUserId { get; set; }

    public DateTimeOffset InstalledAt { get; set; }

    #endregion
}
