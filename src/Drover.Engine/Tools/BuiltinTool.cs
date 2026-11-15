namespace Drover.Engine.Tools;

/// <summary>
/// A tool the server itself implements. Each one is a row in every space's catalog, so it is off until granted and
/// obeys the grant rules like any other tool. <see cref="Name"/> is the catalog name; the model sees it with dots
/// replaced, because model providers do not accept a dot in a function name. A failure the model should read is a
/// <see cref="BuiltinToolException"/>; any other exception reaches the model as "the tool failed" and is logged.
/// </summary>
public sealed record BuiltinTool(
    string Name,
    string Title,
    string Description,
    JsonElement InputSchema,
    ToolSensitivity Sensitivity,
    Func<BuiltinToolCall, CancellationToken, Task<JsonElement>> Run)
{
    #region Properties

    /// <summary>
    /// The tool waits for a person before every use, on every server, whatever its grant says: the tool gate treats
    /// allow as ask, when the agent is built and again when the call is made. A property of the tool, not of a grant,
    /// so no grant, editor, or client can take it away.
    /// </summary>
    public bool AlwaysAsks { get; init; }

    /// <summary>
    /// What a person asked to approve a call should also see that the call's arguments do not say, read on the server
    /// when the call starts to wait. It must only read. Null for a tool that adds nothing.
    /// </summary>
    public Func<BuiltinToolCall, CancellationToken, Task<IReadOnlyList<ToolApprovalDetail>>>? DescribeForApproval { get; init; }

    /// <summary>A hash of what the model is told about the tool. A change to it is a change to the definition.</summary>
    public string DefinitionHash { get; } = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{Name}\n{Description}\n{InputSchema.GetRawText()}")));

    #endregion

    #region Public Methods

    /// <summary>The name the model calls a catalog tool by. Tool server names contain a double underscore; these never do.</summary>
    public static string ModelName(string catalogName) => catalogName.Replace('.', '_');

    #endregion
}
