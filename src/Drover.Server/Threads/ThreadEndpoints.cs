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

        space.MapPost("/threads/{threadId:guid}/messages", PostMessage)
            .WithName("PostMessage")
            .WithSummary("Stores the message and returns the run that answers it. While a run is active the message is pending: it steers that run or waits for it to end, as whenBusy says.");
        space.MapPut("/threads/{threadId:guid}/seen", MarkThreadSeen)
            .WithName("MarkThreadSeen")
            .WithSummary("Marks the thread seen by the caller as far as its newest message, on every screen of theirs.");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<ThreadSummary>, NotFound>> GetThread(Guid threadId, HttpContext context, ThreadService threads) =>
        await threads.FindThread(threadId, context.GetSpaceAccess().UserId, context.RequestAborted) is { } view
            ? TypedResults.Ok(view.Thread.ToSummary(view.ActiveRunId, view.Attention))
            : TypedResults.NotFound();

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
        return TypedResults.Ok(messages
            .Select(message => message.ToResponse(
                message.ScheduleId is { } scheduleId ? crons.GetValueOrDefault(scheduleId) : null,
                message.RunId is { } runId && principals.TryGetValue(runId, out var principal) ? principal : null))
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
}
