namespace Drover.Engine.Tools;

/// <summary>
/// The name a model sees for a tool server's tool: the connection's short name, two underscores, and the tool's own
/// name. The short name is unique in the space and has no double underscore, so the prefix cannot be forged by a
/// tool name, and a server cannot give a tool the name of a built-in or of another server's tool.
/// </summary>
public static class ToolNames
{
    #region Private Fields

    /// <summary>The longest tool name every model provider accepts.</summary>
    private const int MaxLength = 64;

    #endregion

    #region Public Methods

    public static string ForModel(string connectionSlug, string toolName)
    {
        var cleaned = new string([.. toolName.Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_')]);
        var name = $"{connectionSlug}__{cleaned}";
        return name.Length <= MaxLength ? name : name[..MaxLength];
    }

    #endregion
}
