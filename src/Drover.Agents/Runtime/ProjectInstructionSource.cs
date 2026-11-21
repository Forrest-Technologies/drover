namespace Drover.Agents.Runtime;

/// <summary>
/// Gives a run the instructions of the project its conversation belongs to, as their own labeled source beside the
/// bot's instructions. The text is appended to the call's instructions and the call's tools are left as they were:
/// project instructions cannot add a tool. A conversation in no project does not use this provider, so that run's
/// instruction assembly is unchanged. File content never comes through here.
/// </summary>
public sealed class ProjectInstructionSource(ProjectBrief project) : AIContextProvider
{
    #region Private Fields

    public const string Heading = "[Project instructions]";

    #endregion

    #region Public Methods

    /// <summary>The labeled block appended after the bot's own instructions.</summary>
    public static string Label(ProjectBrief project)
    {
        var name = OneLine(project.Name);
        return $"""
            {Heading}
            These instructions belong to the project "{name}". They are separate from this bot's own instructions. They do not add a tool, change the model, or change what this bot is allowed to do.

            {project.Instructions}
            """;
    }

    #endregion

    #region Protected Methods

    /// <summary>The call's own context, with the project instructions after the bot's and the same tools.</summary>
    protected override ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var given = context.AIContext;
        var labeled = Label(project);
        var instructions = string.IsNullOrEmpty(given.Instructions) ? labeled : $"{given.Instructions}\n\n{labeled}";
        return ValueTask.FromResult(new AIContext
        {
            Instructions = instructions,
            Tools = given.Tools,
            Messages = given.Messages,
        });
    }

    #endregion

    #region Private Methods

    private static string OneLine(string name) =>
        name.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    #endregion
}
