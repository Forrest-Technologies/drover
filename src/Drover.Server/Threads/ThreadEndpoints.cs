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
        threads.MapGet("/{threadId:guid}/tool-events", ListToolEvents)
            .WithName("ListToolEvents")
            .WithSummary("The tool events of the thread's runs, as their logs hold them, already redacted, so a client can show past tool calls as it showed them live.");

        space.MapPost("/threads/{threadId:guid}/messages", PostMessage)
            .WithName("PostMessage")
            .WithSummary("Stores the message and returns the run that answers it. While a run is active the message is pending: it steers that run or waits for it to end, as whenBusy says.");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<ThreadSummary>, NotFound>> GetThread(Guid threadId, HttpContext context, ThreadService threads) =>
        await threads.FindThread(threadId, context.RequestAborted) is { } view
            ? TypedResults.Ok(view.Thread.ToSummary(view.ActiveRunId))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<List<MessageResponse>>, NotFound>> ListMessages(
        Guid threadId,
        HttpContext context,
        ThreadService threads)
    {
        var messages = await threads.ListMessages(threadId, context.RequestAborted);
        return messages is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(messages.Select(message => message.ToResponse()).ToList());
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
        var post = await runs.PostMessage(threadId, request.Text ?? string.Empty, steer, access.UserId, context.RequestAborted);

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
