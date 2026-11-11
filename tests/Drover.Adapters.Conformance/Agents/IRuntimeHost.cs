namespace Drover.Adapters.Conformance.Agents;

/// <summary>
/// One running server process: its own service provider and its own runtime. Disposing it without finishing a run
/// is a kill, because nothing the abandoned run does afterwards can reach the database or the tools.
/// </summary>
public interface IRuntimeHost : IAsyncDisposable
{
    IAgentRuntime Runtime { get; }
}


/// <summary>A tool to grant the bot in a conformance run.</summary>
public sealed record ToolSpec(string Name, ToolSensitivity Sensitivity = ToolSensitivity.Read, bool RequiresApproval = false);
