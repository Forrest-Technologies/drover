namespace Drover.Engine.Threads;

/// <summary>A thread and the run still working on it, if any.</summary>
public sealed record ThreadView(ChatThread Thread, Guid? ActiveRunId);

/// <summary>
/// Threads and their messages, as the caller may see them: row-level security shows a person their own threads and
/// the shared ones, so a thread that is not theirs simply is not found. Runs in the tenant scope the endpoint filter
/// opened.
/// </summary>
public sealed class ThreadService(
    ThreadRepository threads,
    MessageRepository messages,
    BotRepository bots,
    RunRepository runs,
    RunEventRepository events,
    TimeProvider time,
    IOptions<DeploymentPolicyOptions> deployment)
{
    #region Private Fields

    private static readonly string[] toolEventTypes =
    [
        RunEventTypes.ToolRequested,
        RunEventTypes.ToolApprovalRequested,
        RunEventTypes.ToolApproved,
        RunEventTypes.ToolDenied,
        RunEventTypes.ToolStarted,
        RunEventTypes.ToolOutput,
        RunEventTypes.ToolCompleted,
    ];

    private const int ListLimit = 200;

    private const int MaxTitleLength = 500;

    #endregion

    #region Public Methods

    /// <summary>The caller's threads with one bot, newest activity first. Null when the bot does not exist here.</summary>
    public async Task<List<ThreadView>?> ListThreads(Guid botId, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is null)
        {
            return null;
        }

        var found = await threads.ListThreadsForBot(botId, ListLimit, cancellationToken);
        var active = await runs.ListActiveRunIds([.. found.Select(thread => thread.Id)], cancellationToken);
        return [.. found.Select(thread => new ThreadView(thread, active.TryGetValue(thread.Id, out var runId) ? runId : null))];
    }

    /// <summary>A new private thread with the bot, owned by the caller. Null when the bot cannot be talked to.</summary>
    public async Task<ChatThread?> Create(Guid botId, string? title, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is not { Status: BotStatus.Active })
        {
            return null;
        }

        var thread = new ChatThread
        {
            BotId = botId,
            OwnerUserId = ownerUserId,
            AudienceUserId = ownerUserId,
            Title = Shorten(title),
            LastActivityAt = time.GetUtcNow(),
        };
        threads.Add(thread);
        return thread;
    }

    /// <summary>
    /// The caller's main thread with the bot, made now when they have none: private to them, with no title. Null
    /// when the bot does not exist here, or when there is no main thread yet and the bot cannot be talked to.
    /// </summary>
    public async Task<ThreadView?> OpenMainThread(Guid botId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is not { } bot)
        {
            return null;
        }

        var main = await threads.FindMainThread(botId, ownerUserId, cancellationToken);

        if (main is null)
        {
            if (bot.Status is not BotStatus.Active)
            {
                return null;
            }

            // Whether this request stored it or a racing one did, the row is there to read now.
            await threads.AddMainThread(botId, ownerUserId, time.GetUtcNow(), cancellationToken);
            main = await threads.FindMainThread(botId, ownerUserId, cancellationToken);
        }

        return main is null ? null : new ThreadView(main, (await runs.FindActiveRun(main.Id, cancellationToken))?.Id);
    }

    public async Task<ThreadView?> FindThread(Guid threadId, CancellationToken cancellationToken)
    {
        var thread = await threads.FindThread(threadId, cancellationToken);
        return thread is null
            ? null
            : new ThreadView(thread, (await runs.FindActiveRun(threadId, cancellationToken))?.Id);
    }

    /// <summary>
    /// The thread's messages, or null when the caller may not see the thread: the conversation in order, then the
    /// messages still pending in the order they were sent.
    /// </summary>
    public async Task<IReadOnlyList<Message>?> ListMessages(Guid threadId, CancellationToken cancellationToken) =>
        await threads.FindThread(threadId, cancellationToken) is null
            ? null
            : [.. await messages.ListMessages(threadId, cancellationToken), .. await messages.ListPendingMessages(threadId, cancellationToken)];

    /// <summary>
    /// What the thread's runs did with their tools, as their logs hold it, or null when the caller may not see the
    /// thread: each run's tool events oldest first, the runs oldest first. Arguments, output, and results in them
    /// were redacted before they were stored. A delegated bot's own calls belong to its own thread and are not here.
    /// </summary>
    public async Task<IReadOnlyList<RunEvent>?> ListToolEvents(Guid threadId, CancellationToken cancellationToken) =>
        await threads.FindThread(threadId, cancellationToken) is null
            ? null
            : await events.ListOwnEventsOfRoots(await runs.ListRootRunIds(threadId, cancellationToken), toolEventTypes, cancellationToken);

    /// <summary>
    /// The thread's running summary, or null when the caller may not see the thread. A side chat has none, and a main
    /// thread has none until the first one is written.
    /// </summary>
    public async Task<ConversationSummary?> GetSummary(Guid threadId, CancellationToken cancellationToken) =>
        await threads.FindThread(threadId, cancellationToken) switch
        {
            null => null,
            { IsMain: true, Summary: { } text } thread =>
                new ConversationSummary(text, thread.SummaryThroughPosition ?? 0, thread.SummaryUpdatedAt, ServerWrites),
            _ => new ConversationSummary(null, 0, null, ServerWrites),
        };

    private bool ServerWrites => deployment.Value.Rules.AllowConversationSummaries;

    public static string? Shorten(string? title) =>
        string.IsNullOrWhiteSpace(title) ? null : title.Trim()[..Math.Min(title.Trim().Length, MaxTitleLength)];

    #endregion
}
