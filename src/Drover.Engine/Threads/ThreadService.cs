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
    TimeProvider time)
{
    #region Private Fields

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

    public async Task<ThreadView?> FindThread(Guid threadId, CancellationToken cancellationToken)
    {
        var thread = await threads.FindThread(threadId, cancellationToken);
        return thread is null
            ? null
            : new ThreadView(thread, (await runs.FindActiveRun(threadId, cancellationToken))?.Id);
    }

    /// <summary>The thread's messages in order, or null when the caller may not see the thread.</summary>
    public async Task<IReadOnlyList<Message>?> ListMessages(Guid threadId, CancellationToken cancellationToken) =>
        await threads.FindThread(threadId, cancellationToken) is null
            ? null
            : await messages.ListMessages(threadId, cancellationToken);

    public static string? Shorten(string? title) =>
        string.IsNullOrWhiteSpace(title) ? null : title.Trim()[..Math.Min(title.Trim().Length, MaxTitleLength)];

    #endregion
}
