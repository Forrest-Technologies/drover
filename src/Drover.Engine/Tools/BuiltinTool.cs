namespace Drover.Engine.Tools;

/// <summary>
/// A tool the server itself implements. Each one is a row in every space's catalog, so it is off until granted and
/// obeys the grant rules like any other tool. <see cref="Name"/> is the catalog name; the model sees it with dots
/// replaced, because model providers do not accept a dot in a function name.
/// </summary>
public sealed record BuiltinTool(
    string Name,
    string Title,
    string Description,
    JsonElement InputSchema,
    ToolSensitivity Sensitivity,
    Func<JsonElement, CancellationToken, Task<JsonElement>> Run)
{
    #region Properties

    /// <summary>A hash of what the model is told about the tool. A change to it is a change to the definition.</summary>
    public string DefinitionHash { get; } = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{Name}\n{Description}\n{InputSchema.GetRawText()}")));

    #endregion

    #region Public Methods

    /// <summary>The name the model calls a catalog tool by. Tool server names contain a double underscore; these never do.</summary>
    public static string ModelName(string catalogName) => catalogName.Replace('.', '_');

    #endregion
}
