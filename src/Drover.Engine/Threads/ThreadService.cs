namespace Drover.Engine.Threads;

/// <summary>A thread, the run still working on it, if any, and what it has for the person who asked.</summary>
public sealed record ThreadView(ChatThread Thread, Guid? ActiveRunId, ThreadAttention? Attention = null);

/// <summary>What choosing a conversation's model came to: the models now, or why the choice was refused. Neither when the thread is not found.</summary>
public sealed record ModelChoice(ConversationModelChoice? Choice, string? Problem = null);

public enum ProjectMembershipOutcome
{
    Updated,
    NotFound,
    Forbidden,
}

/// <summary>What moving a conversation into or out of a project came to.</summary>
public sealed record ProjectMembershipChange(ProjectMembershipOutcome Outcome, ThreadView? Thread = null);

/// <summary>
/// Threads and their messages, as the caller may see them: row-level security shows a person their own threads and
/// the shared ones, so a thread that is not theirs simply is not found. Runs in the tenant scope the endpoint filter
/// opened, except marking a thread seen, which opens its own so the person's other screens are told after the commit.
/// </summary>
public sealed class ThreadService(
    ThreadRepository threads,
    ProjectRepository projects,
    MessageRepository messages,
    BotRepository bots,
    RunRepository runs,
    RunEventRepository events,
    BotScheduleRepository schedules,
    TenantScope scope,
    UserSignalHub signals,
    ConversationModels models,
    SandboxRepository sandboxes,
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
        RunEventTypes.DelegationStarted,
        RunEventTypes.DelegationCompleted,
    ];

    // A delegated bot's work is shown inside the call that handed it over, so its words and how its run went come
    // with its calls. The root's own words are the thread's messages.
    private static readonly string[] delegatedEventTypes =
    [
        .. toolEventTypes,
        RunEventTypes.MessageCompleted,
        RunEventTypes.RunQueued,
        RunEventTypes.RunStarted,
        RunEventTypes.RunWaiting,
        RunEventTypes.RunCompleted,
        RunEventTypes.RunFailed,
        RunEventTypes.RunCancelled,
    ];

    private const int ListLimit = 200;

    private const int MaxTitleLength = 500;

    #endregion

    #region Public Methods

    /// <summary>The caller's own unarchived conversations across bots in the space, newest activity first.</summary>
    public async Task<List<ThreadView>> ListOwnedThreads(Guid userId, CancellationToken cancellationToken)
    {
        var found = await threads.ListOwnedThreads(userId, ListLimit, cancellationToken);
        var active = await runs.ListActiveRunIds([.. found.Select(thread => thread.Id)], cancellationToken);
        return await WithAttention(
            [.. found.Select(thread => new ThreadView(thread, active.TryGetValue(thread.Id, out var runId) ? runId : null))],
            userId,
            cancellationToken);
    }

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

    /// <summary>
    /// The caller's visible threads in one project, newest activity first, each with what it has for them. Null when
    /// the project does not exist here.
    /// </summary>
    public async Task<List<ThreadView>?> ListThreadsForProject(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        if (await projects.FindProject(projectId, cancellationToken) is null)
        {
            return null;
        }

        var found = await threads.ListThreadsForProject(projectId, ListLimit, cancellationToken);
        var active = await runs.ListActiveRunIds([.. found.Select(thread => thread.Id)], cancellationToken);
        return await WithAttention(
            [.. found.Select(thread => new ThreadView(thread, active.TryGetValue(thread.Id, out var runId) ? runId : null))],
            userId,
            cancellationToken);
    }

    /// <summary>A new private thread with the bot, owned by the caller. Null when the bot or project cannot be used.</summary>
    public async Task<ChatThread?> Create(
        Guid botId,
        string? title,
        Guid ownerUserId,
        CancellationToken cancellationToken,
        Guid? projectId = null)
    {
        if (await bots.FindBot(botId, cancellationToken) is not { Status: BotStatus.Active })
        {
            return null;
        }

        if (projectId is { } targetProjectId && await projects.LockProjectForShare(targetProjectId, cancellationToken) is null)
        {
            return null;
        }

        var thread = new ChatThread
        {
            BotId = botId,
            ProjectId = projectId,
            OwnerUserId = ownerUserId,
            AudienceUserId = ownerUserId,
            Title = Shorten(title),
            LastActivityAt = time.GetUtcNow(),
        };
        threads.Add(thread);
        return thread;
    }

    /// <summary>
    /// Moves a conversation into a project of the same space, or out of its project when
    /// <paramref name="projectId"/> is null. Only the conversation's owner may move it; another member gets forbidden
    /// on a shared conversation, and row-level security hides private conversations of other people as not found.
    /// </summary>
    public async Task<ProjectMembershipChange> SetProject(
        Guid threadId,
        Guid? projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (await threads.FindThread(threadId, cancellationToken) is not { } existing)
        {
            return new(ProjectMembershipOutcome.NotFound);
        }

        if (existing.OwnerUserId != userId)
        {
            return new(ProjectMembershipOutcome.Forbidden);
        }

        if (projectId is { } targetProjectId && await projects.LockProjectForShare(targetProjectId, cancellationToken) is null)
        {
            return new(ProjectMembershipOutcome.NotFound);
        }

        if (await threads.GetThreadForUpdate(threadId, cancellationToken) is not { } thread)
        {
            return new(ProjectMembershipOutcome.NotFound);
        }

        thread.ProjectId = projectId;
        return new(ProjectMembershipOutcome.Updated, await WithAttention(thread, userId, cancellationToken));
    }

    /// <summary>
    /// Makes a shared thread private to its owner. Every live sandbox is retired in this same transaction, before
    /// the audience narrows: a copy was allowed because the whole space could see the conversation, and that is no
    /// longer true. Nothing on the sandbox says which conversation wrote it, so every live sandbox of the space is
    /// retired. The retirement holds the space's copy fence, so a copy still in flight re-checks the audience before
    /// it writes. False when the caller is not the owner, or the thread is not there. A thread that is already
    /// private is left as it is.
    /// </summary>
    public async Task<bool> MakePrivate(Guid threadId, Guid userId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var thread = await threads.FindThread(threadId, cancellationToken);

        if (thread is null || thread.OwnerUserId != userId)
        {
            return false;
        }

        if (thread.AudienceUserId == Audience.Everyone)
        {
            await sandboxes.RetireLive(time.GetUtcNow(), cancellationToken);

            if (!await threads.MakePrivate(threadId, userId, cancellationToken))
            {
                return false;
            }
        }

        await work.Commit(cancellationToken);
        return true;
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

    /// <summary>The versions of each place in the thread that has more than one, by the id of every version.</summary>
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListVersions(Guid threadId, CancellationToken cancellationToken) =>
        messages.ListVersions(threadId, cancellationToken);

    /// <summary>The person each run that wrote one of the messages acts for, by run.</summary>
    public async Task<Dictionary<Guid, Guid>> ListRunPrincipals(IReadOnlyList<Message> sent, CancellationToken cancellationToken) =>
        sent.Any(message => message.RunId is not null)
            ? await runs.ListPrincipals([.. sent.Select(message => message.RunId).OfType<Guid>().Distinct()], cancellationToken)
            : [];

    /// <summary>
    /// The time of each schedule that sent one of these messages and still exists, by schedule id, so the conversation
    /// can say which schedule sent it.
    /// </summary>
    public async Task<Dictionary<Guid, string>> ListScheduleCrons(IReadOnlyList<Message> sent, CancellationToken cancellationToken) =>
        sent.Any(message => message.ScheduleId is not null)
            ? await schedules.ListCrons([.. sent.Select(message => message.ScheduleId).OfType<Guid>().Distinct()], cancellationToken)
            : [];

    /// <summary>
    /// What the thread's runs did with their tools, as their logs hold it, or null when the caller may not see the
    /// thread: each run's tool and delegation events oldest first, the runs oldest first. With them come the events of
    /// the runs those delegations started, at any depth: their tool and delegation events, their messages, and their
    /// status, so the work handed over reads as it did live. Arguments, output, and results in them were redacted
    /// before they were stored. A delegated run's thread has the root thread's audience, so row-level security lets
    /// exactly the same people read both. A thread a delegation made has no root run: its runs are children, and what
    /// they did and handed further down is listed the same way, from them.
    /// </summary>
    public async Task<IReadOnlyList<RunEvent>?> ListToolEvents(Guid threadId, CancellationToken cancellationToken) =>
        await threads.FindThread(threadId, cancellationToken) switch
        {
            null => null,
            { Origin: ThreadOrigin.Delegation } => await events.ListEventsBelowThread(threadId, toolEventTypes, delegatedEventTypes, cancellationToken),
            _ => await events.ListEventsOfTrees(
                await runs.ListRootRunIds(threadId, cancellationToken),
                toolEventTypes,
                delegatedEventTypes,
                cancellationToken),
        };

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

    /// <summary>The models the conversation may answer with, or null when the caller may not see the thread.</summary>
    public async Task<ConversationModelChoice?> GetModels(Guid threadId, CancellationToken cancellationToken) =>
        await threads.FindThread(threadId, cancellationToken) is { } thread && await bots.FindBot(thread.BotId, cancellationToken) is { } bot
            ? await DescribeModels(thread, bot, cancellationToken)
            : null;

    /// <summary>
    /// Makes the conversation answer with <paramref name="model"/> from its next run, for anyone who may write in it.
    /// The bot's own model, or none, goes back to the bot's. A model the bot may not use is refused with the reason.
    /// </summary>
    public async Task<ModelChoice> ChooseModel(Guid threadId, string? model, CancellationToken cancellationToken)
    {
        if (await threads.GetThreadForUpdate(threadId, cancellationToken) is not { } thread
            || await bots.FindBot(thread.BotId, cancellationToken) is not { } bot)
        {
            return new ModelChoice(null);
        }

        var chosen = string.IsNullOrWhiteSpace(model) || model.Trim() == bot.Model ? null : model.Trim();

        if (chosen is { Length: > BotService.MaxModelLength })
        {
            return new ModelChoice(null, $"A model's name is at most {BotService.MaxModelLength} characters.");
        }

        if (chosen is not null && await models.FindProblem(bot, chosen, cancellationToken) is { } problem)
        {
            return new ModelChoice(null, problem);
        }

        thread.Model = chosen;
        return new ModelChoice(await DescribeModels(thread, bot, cancellationToken));
    }

    public static string? Shorten(string? title) =>
        string.IsNullOrWhiteSpace(title) ? null : title.Trim()[..Math.Min(title.Trim().Length, MaxTitleLength)];

    #endregion

    #region Private Methods

    // A choice the bot may no longer use is not shown as chosen: its runs answer with the bot's own model.
    private async Task<ConversationModelChoice> DescribeModels(ChatThread thread, Bot bot, CancellationToken cancellationToken)
    {
        var allowed = await models.ListAllowed(bot, cancellationToken);
        var answering = await models.ModelFor(thread, bot, cancellationToken);
        var botModelName = allowed.FirstOrDefault(entry => entry.Model == bot.Model)?.DisplayName ?? bot.Model;
        return new ConversationModelChoice(
            bot.Model,
            botModelName,
            answering == bot.Model ? null : answering,
            [.. allowed.Select(entry => new ConversationModelOption(entry.Model, entry.DisplayName))]);
    }

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
