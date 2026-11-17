namespace Drover.Contracts.Extensions;

/// <summary>
/// An extension as a file: one JSON document a person shares, which adds a bot to a space. It carries no credential
/// and grants nothing. <see cref="Version"/> is the format's version, <see cref="Id"/> names the extension wherever
/// it is installed (lower-case letters, digits, dots, and hyphens), and <see cref="Contents"/> is what it adds, each
/// part told apart by its <c>kind</c>. A server refuses a file with a part or a field it does not know.
/// </summary>
public sealed record ExtensionPackage(int Version, string Id, string Name)
{
    #region Properties

    public const int CurrentVersion = 1;

    /// <summary>What the extension is for, in its author's words. Shown as plain text.</summary>
    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<ExtensionContent> Contents { get; init; } = [];

    #endregion
}

/// <summary>One thing an extension adds.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(BotTemplate), BotTemplate.Kind)]
public abstract record ExtensionContent;

/// <summary>
/// A bot to copy into the space. These are all the fields a template has: a bot's sandbox, limits, schedules,
/// memory, and grants are never part of a file. <see cref="Instructions"/> is a stranger's text until a person has
/// read it. <see cref="SuggestedModel"/> is advice: the person importing chooses the model.
/// </summary>
public sealed record BotTemplate(string Name, string Instructions) : ExtensionContent
{
    #region Properties

    public const string Kind = "bot";

    /// <summary>What other bots read to decide whether to hand this one a task.</summary>
    public string Description { get; init; } = string.Empty;

    public string? SuggestedModel { get; init; }

    #endregion
}
