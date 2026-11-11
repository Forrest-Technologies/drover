namespace Drover.Agents.Sessions;

/// <summary>
/// Keeps the framework's serialized session in <c>agent_sessions</c>, one row per thread and bot. The key's space and
/// audience partitions name the tenant; row-level security on the table is what enforces it.
/// </summary>
/// <remarks>
/// A session is opaque working state stored with the framework version that wrote it. A row written by another
/// version, or one that no longer deserializes, is discarded: the conversation itself is in <c>messages</c>, so the
/// run continues from there with a new session.
/// </remarks>
public sealed class PostgresAgentSessionStore(RunContext run, ILogger logger) : AgentSessionStore
{
    #region Partition Names

    public const string SpacePartition = "space";

    public const string AudiencePartition = "audience";

    #endregion

    #region Properties

    public static string FrameworkVersion { get; } =
        typeof(AIAgent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AIAgent).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    #endregion

    #region Public Methods

    public static AgentSessionStoreKey KeyFor(RunContext run) =>
        new AgentSessionStoreKey($"{run.Thread.Id:N}/{run.Execution.Bot.BotId:N}", null)
            .WithPartition(SpacePartition, run.Thread.SpaceId.ToString("N"))
            .WithPartition(AudiencePartition, run.Thread.AudienceUserId.ToString("N"));

    public override async ValueTask SaveSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        RequireThisRun(key);
        var state = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        await run.Database.Transact(
            services => services.GetRequiredService<AgentSessionRepository>()
                .SaveSession(run.Thread, run.Execution.Bot.BotId, state, FrameworkVersion, cancellationToken),
            cancellationToken);
    }

    public override async ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
    {
        RequireThisRun(key);
        var stored = await run.Database.Transact(
            services => services.GetRequiredService<AgentSessionRepository>()
                .GetSession(run.Thread.Id, run.Execution.Bot.BotId, cancellationToken),
            cancellationToken);

        if (stored is null)
        {
            return null;
        }

        if (stored.FrameworkVersion != FrameworkVersion)
        {
            logger.LogInformation(
                "Discarding the session of thread {ThreadId}: written by framework {Stored}, running {Current}",
                run.Thread.Id,
                stored.FrameworkVersion,
                FrameworkVersion);
            await DeleteSessionAsync(agent, key, cancellationToken);
            return null;
        }

        return await Deserialize(agent, key, stored.State, cancellationToken);
    }

    public override async ValueTask DeleteSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
    {
        RequireThisRun(key);
        await run.Database.Transact(
            services => services.GetRequiredService<AgentSessionRepository>()
                .DeleteSession(run.Thread.Id, run.Execution.Bot.BotId, cancellationToken),
            cancellationToken);
    }

    #endregion

    #region Private Methods

    private async Task<AgentSession?> Deserialize(
        AIAgent agent,
        AgentSessionStoreKey key,
        JsonElement state,
        CancellationToken cancellationToken)
    {
        try
        {
            return await agent.DeserializeSessionAsync(state, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            logger.LogWarning(exception, "Discarding the session of thread {ThreadId}: it does not deserialize", run.Thread.Id);
            await DeleteSessionAsync(agent, key, cancellationToken);
            return null;
        }
    }

    /// <summary>The store serves one run; a key for another thread or tenant is a bug, never a lookup.</summary>
    private void RequireThisRun(AgentSessionStoreKey key)
    {
        if (!key.Equals(KeyFor(run)))
        {
            throw new InvalidOperationException("This session store serves one run's thread and bot only.");
        }
    }

    #endregion
}
