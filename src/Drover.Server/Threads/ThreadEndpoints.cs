namespace Drover.Server.Threads;

/// <summary>
/// Threads and messages. Reads run under the tenant scope filter, where row-level security hides another person's
/// private thread, so it is simply not found. Posting a message starts a run, or hands the message to the one under
/// way, and tells the workers after the commit, so that endpoint sits outside the filter and the service opens its
/// own scope.
/// </summary>
public static class ThreadEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapThreadEndpoints(this RouteGroupBuilder space)
    {
        var threads = space.MapGroup("/threads").AddEndpointFilter<TenantScopeFilter>();
        threads.MapGet("/search", SearchMessages)
            .WithName("SearchMessages")
            .WithSummary("The messages of the caller's conversations whose words match q, best match first, each with a snippet around the match. Only the threads the caller may read are searched.");
        threads.MapGet("/{threadId:guid}", GetThread).WithName("GetThread");
        threads.MapGet("/{threadId:guid}/messages", ListMessages).WithName("ListMessages");
        threads.MapGet("/{threadId:guid}/summary", GetConversationSummary)
            .WithName("GetConversationSummary")
            .WithSummary("The server's running summary of a main thread. Text is null until one is written, and always for a side chat.");
        threads.MapGet("/{threadId:guid}/tool-events", ListToolEvents)
            .WithName("ListToolEvents")
            .WithSummary("The tool events of the thread's runs, as their logs hold them, already redacted, so a client can show past tool calls as it showed them live.");
        threads.MapGet("/{threadId:guid}/models", GetModels)
            .WithName("GetConversationModels")
            .WithSummary("The models the conversation may answer with: the bot's own, the one chosen in its place, and every model it may choose.");
        threads.MapPut("/{threadId:guid}/model", ChooseModel)
            .WithName("ChooseConversationModel")
            .WithSummary("Makes the conversation answer with another of the bot's models from its next run. Null goes back to the bot's own. A model the bot may not use, or one that could cost more than the space allows, is refused.");
        threads.MapPut("/{threadId:guid}/project", SetProject)
            .WithName("SetThreadProject")
            .WithSummary("Moves a conversation into a project of the same space, or out of its project when projectId is null. Only the conversation's owner may move it.");

        space.MapPost("/threads/{threadId:guid}/messages", PostMessage)
            .WithName("PostMessage")
            .WithSummary("Stores the message and returns the run that answers it. While a run is active the message is pending: it steers that run or waits for it to end, as whenBusy says.");
        space.MapPost("/threads/{threadId:guid}/messages/{messageId:guid}/regenerate", Regenerate)
            .WithName("RegenerateReply")
            .WithSummary("Answers again the person's message a bot's reply answers. The reply and everything after it are kept as the earlier version; the new run answers with the conversation's current model. Only the message's author may, while no run works on the thread.");
        space.MapPost("/threads/{threadId:guid}/messages/{messageId:guid}/edit", EditMessage)
            .WithName("EditMessage")
            .WithSummary("Stores a new version of the caller's message in its place and answers it with a new run. The message and everything after it are kept as the earlier version.");
        space.MapPost("/threads/{threadId:guid}/messages/{messageId:guid}/show", ShowVersion)
            .WithName("ShowMessageVersion")
            .WithSummary("Shows this version of its place in the conversation, with what followed it, in place of the version shown. The next run reads the version shown.");
        space.MapPut("/threads/{threadId:guid}/seen", MarkThreadSeen)
            .WithName("MarkThreadSeen")
            .WithSummary("Marks the thread seen by the caller as far as its newest message, on every screen of theirs.");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<List<MessageSearchHit>>, ProblemHttpResult>> SearchMessages(
        string? q,
        HttpContext context,
        ConversationSearchService search) =>
        ConversationSearchService.Refusal(q) is { } refusal
            ? TypedResults.Problem(title: refusal, statusCode: StatusCodes.Status400BadRequest)
            : TypedResults.Ok(await search.Search(q!, context.RequestAborted));

    private static async Task<Results<Ok<ThreadSummary>, NotFound>> GetThread(Guid threadId, HttpContext context, ThreadService threads) =>
        await threads.FindThread(threadId, context.GetSpaceAccess().UserId, context.RequestAborted) is { } view
            ? TypedResults.Ok(view.Thread.ToSummary(view.ActiveRunId, view.Attention))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<ThreadSummary>, NotFound, ForbidHttpResult>> SetProject(
        Guid threadId,
        SetThreadProjectRequest request,
        HttpContext context,
        ThreadService threads)
    {
        var outcome = await threads.SetProject(threadId, request.ProjectId, context.GetSpaceAccess().UserId, context.RequestAborted);
        return outcome switch
        {
            { Outcome: ProjectMembershipOutcome.Updated, Thread: { } view } =>
                TypedResults.Ok(view.Thread.ToSummary(view.ActiveRunId, view.Attention)),
            { Outcome: ProjectMembershipOutcome.Forbidden } => TypedResults.Forbid(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound>> MarkThreadSeen(Guid threadId, HttpContext context, ThreadService threads) =>
        await threads.MarkSeen(threadId, context.GetSpaceAccess().UserId, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<Ok<ConversationSummary>, NotFound>> GetConversationSummary(
        Guid threadId,
        HttpContext context,
        ThreadService threads) =>
        await threads.GetSummary(threadId, context.RequestAborted) is { } summary
            ? TypedResults.Ok(summary)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<ConversationModelChoice>, NotFound>> GetModels(Guid threadId, HttpContext context, ThreadService threads) =>
        await threads.GetModels(threadId, context.RequestAborted) is { } choice
            ? TypedResults.Ok(choice)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<ConversationModelChoice>, NotFound, ProblemHttpResult>> ChooseModel(
        Guid threadId,
        ChooseModelRequest request,
        HttpContext context,
        ThreadService threads)
    {
        var outcome = await threads.ChooseModel(threadId, request.Model, context.RequestAborted);
        return outcome switch
        {
            { Choice: { } choice } => TypedResults.Ok(choice),
            { Problem: { } problem } => TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<List<MessageResponse>>, NotFound>> ListMessages(
        Guid threadId,
        HttpContext context,
        ThreadService threads)
    {
        if (await threads.ListMessages(threadId, context.RequestAborted) is not { } messages)
        {
            return TypedResults.NotFound();
        }

        var crons = await threads.ListScheduleCrons(messages, context.RequestAborted);
        var principals = await threads.ListRunPrincipals(messages, context.RequestAborted);
        var versions = await threads.ListVersions(threadId, context.RequestAborted);
        return TypedResults.Ok(messages
            .Select(message => message.ToResponse(
                message.ScheduleId is { } scheduleId ? crons.GetValueOrDefault(scheduleId) : null,
                message.RunId is { } runId && principals.TryGetValue(runId, out var principal) ? principal : null,
                versions.GetValueOrDefault(message.Id)))
            .ToList());
    }

    private static async Task<Results<Ok<List<RunEventResponse>>, NotFound>> ListToolEvents(
        Guid threadId,
        HttpContext context,
        ThreadService threads)
    {
        var events = await threads.ListToolEvents(threadId, context.RequestAborted);
        return events is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(events.Select(stored => stored.ToResponse()).ToList());
    }

    private static async Task<Results<Accepted<SendMessageResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> Regenerate(
        Guid threadId,
        Guid messageId,
        HttpContext context,
        RunService runs)
    {
        var access = context.GetSpaceAccess();
        return Started(access, await runs.Regenerate(threadId, messageId, access.UserId, context.RequestAborted));
    }

    private static async Task<Results<Accepted<SendMessageResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> EditMessage(
        Guid threadId,
        Guid messageId,
        EditMessageRequest request,
        HttpContext context,
        RunService runs)
    {
        var access = context.GetSpaceAccess();
        return Started(access, await runs.Edit(threadId, messageId, request.Text ?? string.Empty, access.UserId, context.RequestAborted));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ShowVersion(
        Guid threadId,
        Guid messageId,
        HttpContext context,
        RunService runs)
    {
        var change = await runs.ShowVersion(threadId, messageId, context.GetSpaceAccess().UserId, context.RequestAborted);
        return change.Outcome switch
        {
            VersionOutcome.Shown => TypedResults.NoContent(),
            VersionOutcome.NotFound => TypedResults.NotFound(),
            _ => VersionProblem(change),
        };
    }

    private static async Task<Results<Accepted<SendMessageResponse>, NotFound, ProblemHttpResult>> PostMessage(
        Guid threadId,
        SendMessageRequest request,
        HttpContext context,
        RunService runs)
    {
        if (request.WhenBusy is not (null or BusyModes.Steer or BusyModes.Queue))
        {
            return TypedResults.Problem(title: $"whenBusy is '{BusyModes.Steer}' or '{BusyModes.Queue}'.", statusCode: StatusCodes.Status400BadRequest);
        }

        var access = context.GetSpaceAccess();
        var steer = request.WhenBusy is not BusyModes.Queue;
        var post = await runs.PostMessage(threadId, request.Text ?? string.Empty, request.FileIds ?? [], steer, access.UserId, context.RequestAborted);

        return post.Problem switch
        {
            PostProblem.NotFound => TypedResults.NotFound(),
            PostProblem.Invalid => TypedResults.Problem(title: post.Detail, statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.Accepted(
                $"/api/spaces/{access.SpaceId}/runs/{post.Run!.Id}",
                new SendMessageResponse(post.Message!.ToResponse(), post.Run.ToSummary())),
        };
    }

    #endregion

    #region Versions

    private static Results<Accepted<SendMessageResponse>, NotFound, ForbidHttpResult, ProblemHttpResult> Started(SpaceAccess access, VersionChange change) =>
        change.Outcome switch
        {
            VersionOutcome.Queued => TypedResults.Accepted(
                $"/api/spaces/{access.SpaceId}/runs/{change.Run!.Id}",
                new SendMessageResponse(change.Message!.ToResponse(), change.Run.ToSummary())),
            VersionOutcome.NotFound => TypedResults.NotFound(),
            VersionOutcome.Forbidden => TypedResults.Forbid(),
            _ => VersionProblem(change),
        };

    private static ProblemHttpResult VersionProblem(VersionChange change) =>
        change.Outcome switch
        {
            VersionOutcome.Busy => TypedResults.Problem(
                title: "The bot is still working in this conversation. Wait for it to finish, or stop it.",
                statusCode: StatusCodes.Status409Conflict),
            VersionOutcome.BotUnavailable => TypedResults.Problem(
                title: "This bot is paused or removed, so it cannot answer.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(title: change.Detail, statusCode: StatusCodes.Status400BadRequest),
        };

    #endregion
}
