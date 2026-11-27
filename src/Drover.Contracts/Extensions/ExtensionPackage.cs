namespace Drover.Contracts.Extensions;

/// <summary>
/// An extension as a file: one JSON document a person shares, which adds things a space can pick up. It holds data
/// and no code, carries no credential, and grants nothing. <see cref="Version"/> is the format's version,
/// <see cref="Id"/> names the extension wherever it is installed (lower-case letters, digits, dots, and hyphens),
/// <see cref="Settings"/> are the options a person fills in for it, and <see cref="Contents"/> is what it adds, each
/// part told apart by its <c>kind</c>. A server refuses a file with a part or a field it does not know.
/// </summary>
public sealed record ExtensionPackage(int Version, string Id, string Name)
{
    #region Properties

    public const int CurrentVersion = 2;

    public const int MaxIdLength = 100;

    /// <summary>What the extension is for, in its author's words. Shown as plain text.</summary>
    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<ExtensionSetting> Settings { get; init; } = [];

    public IReadOnlyList<ExtensionContent> Contents { get; init; } = [];

    #endregion
}

/// <summary>
/// An option of the extension that a person fills in. <see cref="Type"/> is <c>text</c>, <c>number</c>,
/// <c>choice</c>, <c>toggle</c>, or <c>secret</c>. A part of the file uses the value by writing <c>{key}</c>.
/// <see cref="Choices"/> are what a <c>choice</c> offers. <see cref="Default"/> is what the field starts with, and a
/// secret has none. A secret is typed when the tool server is connected, goes to the vault, and is never kept with
/// the extension or returned.
/// </summary>
public sealed record ExtensionSetting(string Key, string Label, string Type)
{
    #region Properties

    public const string Text = "text";

    public const string Number = "number";

    public const string Choice = "choice";

    public const string Toggle = "toggle";

    public const string Secret = "secret";

    /// <summary>A sentence under the field that says what to type or where to find it. Shown as plain text.</summary>
    public string Help { get; init; } = string.Empty;

    public IReadOnlyList<string> Choices { get; init; } = [];

    public string? Default { get; init; }

    #endregion
}

/// <summary>One thing an extension adds.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(ConnectionRecipe), ConnectionRecipe.Kind)]
[JsonDerivedType(typeof(ThemePart), ThemePart.Kind)]
[JsonDerivedType(typeof(RuleTemplate), RuleTemplate.Kind)]
public abstract record ExtensionContent;

/// <summary>
/// A tool server to connect. These are all the fields a recipe has: it names where the server is and how Drover
/// proves who is calling, and holds no credential, no grant, and no say over how far the server is trusted.
/// <see cref="Auth"/> is <c>none</c>, <c>header</c>, or <c>oauth</c>; <see cref="HeaderName"/> is the header's
/// name where there is one. <see cref="Url"/> and <see cref="HeaderTemplate"/> may hold <c>{key}</c> where a setting
/// goes, and only <see cref="HeaderTemplate"/> may use a secret one. With a header and no <see cref="HeaderTemplate"/>,
/// the person who connects types the whole value. <see cref="SuggestedTools"/> are advice: a person gives tools to a
/// bot.
/// </summary>
public sealed record ConnectionRecipe(string Name, string Url) : ExtensionContent
{
    #region Properties

    public const string Kind = "mcp_connection";

    public string Auth { get; init; } = "none";

    public string? HeaderName { get; init; }

    public string? HeaderTemplate { get; init; }

    public IReadOnlyList<SuggestedTool> SuggestedTools { get; init; } = [];

    #endregion
}

/// <summary>A tool the extension suggests giving to a bot, by the name the server gives it, and why. Shown as plain text.</summary>
public sealed record SuggestedTool(string Name, string Why = "");

/// <summary>
/// A theme the space's members can choose. <see cref="Theme"/> is a theme file, whole, in the closed theme format.
/// It is checked with the checker every theme passes before the extension is added, and again on the device before
/// it is offered.
/// </summary>
public sealed record ThemePart(JsonElement Theme) : ExtensionContent
{
    #region Properties

    public const string Kind = "theme";

    #endregion
}

/// <summary>
/// A rule a person can add to a bot. The extension gives it to no bot: it is offered where a bot's rules are
/// edited, and added there the way a rule written by hand is. The words are the ones a bot's rule uses.
/// </summary>
public sealed record RuleTemplate(string Name, string Checkpoint, string Effect) : ExtensionContent
{
    #region Properties

    public const string Kind = "rule";

    public IReadOnlyList<RuleConditionBody> Conditions { get; init; } = [];

    #endregion
}
