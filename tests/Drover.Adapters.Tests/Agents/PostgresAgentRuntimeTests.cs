namespace Drover.Adapters.Tests.Agents;

/// <summary>The agent framework runtime with PostgreSQL storage, run through the runtime conformance suite.</summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class PostgresAgentRuntimeTests(PostgresFixture postgres) : AgentRuntimeConformance, IAsyncLifetime
{
    #region Private Fields

    private ServiceProvider services = null!; // set in InitializeAsync

    private AgentWorld world = null!; // set in InitializeAsync

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        services = postgres.NewServices().BuildServiceProvider();
        world = await AgentWorld.Create(services);
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();

    #endregion

    #region Harness

    protected override Task<RunExecution> NewRun(string userText, params ToolSpec[] tools) => world.NewRun(userText, tools);

    protected override Task<RunExecution> ContinueThread(RunExecution previous, string userText) =>
        world.ContinueThread(previous, userText);

    protected override IRuntimeHost StartHost(ScriptedModel model) => new AgentRuntimeHost(postgres, model);

    protected override Task<IReadOnlyList<Message>> ReadMessages(RunExecution run) =>
        world.Read<IReadOnlyList<Message>>(async db =>
            await db.Messages.Where(message => message.ThreadId == run.ThreadId).OrderBy(message => message.Position).ToListAsync());

    protected override Task<IReadOnlyList<ToolCall>> ReadToolCalls(RunExecution run) =>
        world.Read<IReadOnlyList<ToolCall>>(async db =>
            await db.ToolCalls.Where(call => call.RunId == run.RunId).ToListAsync());

    #endregion
}
