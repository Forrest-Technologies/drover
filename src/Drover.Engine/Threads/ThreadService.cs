namespace Drover.Engine.Threads;

/// <summary>A thread, the run still working on it, if any, and what it has for the person who asked.</summary>
public sealed record ThreadView(ChatThread Thread, Guid? ActiveRunId, ThreadAttention? Attention = null);

/// <summary>
/// Threads and their messages, as the caller may see them: row-level security shows a person their own threads and
/// the shared ones, so a thread that is not theirs simply is not found. Runs in the tenant scope the endpoint filter
/// opened, except marking a thread seen, which opens its own so the person's other screens are told after the commit.
/// </summary>
public sealed class ThreadService(
    ThreadRepository threads,
    MessageRepository messages,
    BotRepository bots,
    RunRepository runs,
    RunEventRepository events,
    TenantScope scope,
    UserSignalHub signals,
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

    /// <summary>
    /// The caller's threads with one bot, newest activity first, each with what it has for them. Null when the bot
    /// does not exist here.
    /// </summary>
    public async Task<List<ThreadView>?> ListThreads(Guid botId, Guid userId, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is null)
        {
            return null;
        }

        var found = await threads.ListThreadsForBot(botId, ListLimit, cancellationToken);
        var active = await runs.ListActiveRunIds([.. found.Select(thread => thread.Id)], cancellationToken);
        return await WithAttention(
            [.. found.Select(thread => new ThreadView(thread, active.TryGetValue(thread.Id, out var runId) ? runId : null))],
            userId,
            cancellationToken);
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

        return main is null ? null : await WithAttention(main, ownerUserId, cancellationToken);
    }

    public async Task<ThreadView?> FindThread(Guid threadId, Guid userId, CancellationToken cancellationToken)
    {
        var thread = await threads.FindThread(threadId, cancellationToken);
        return thread is null ? null : await WithAttention(thread, userId, cancellationToken);
    }

    /// <summary>
    /// Marks the thread seen by the caller as far as its newest message, then tells their other screens. False when
    /// the caller may not see the thread.
    /// </summary>
    public async Task<bool> MarkSeen(Guid threadId, Guid userId, CancellationToken cancellationToken)
    {
        ChatThread? thread;

        await using (var work = await scope.Begin(cancellationToken))
        {
            thread = await threads.FindThread(threadId, cancellationToken);

            if (thread is null)
            {
                return false;
            }

            await threads.MarkSeen(thread, userId, time.GetUtcNow(), cancellationToken);
            await work.Commit(cancellationToken);
        }

        signals.Publish(thread.SpaceId, userId, new UserSignal(UserSignalKinds.ThreadSeen, thread.Id, null));
        return true;
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

    #region Private Methods

    private async Task<ThreadView> WithAttention(ChatThread thread, Guid userId, CancellationToken cancellationToken)
    {
        var view = new ThreadView(thread, (await runs.FindActiveRun(thread.Id, cancellationToken))?.Id);
        return (await WithAttention([view], userId, cancellationToken))[0];
    }

    private async Task<List<ThreadView>> WithAttention(List<ThreadView> views, Guid userId, CancellationToken cancellationToken)
    {
        var facts = await threads.ListFacts([.. views.Select(view => view.Thread.Id)], userId, cancellationToken);
        return [.. views.Select(view => view with { Attention = facts[view.Thread.Id].ToAttention() })];
    }

    #endregion
}
