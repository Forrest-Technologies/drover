namespace Drover.Server.Threads;

/// <summary>
/// Threads and messages. Reads run under the tenant scope filter, where row-level security hides another person's
/// private thread, so it is simply not found. Posting a message starts a run and tells the workers after the commit,
/// so that endpoint sits outside the filter and the service opens its own scope.
/// </summary>
public static class ThreadEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapThreadEndpoints(this RouteGroupBuilder space)
    {
        var threads = space.MapGroup("/threads").AddEndpointFilter<TenantScopeFilter>();
        threads.MapGet("/{threadId:guid}", GetThread).WithName("GetThread");
        threads.MapGet("/{threadId:guid}/messages", ListMessages).WithName("ListMessages");

        space.MapPost("/threads/{threadId:guid}/messages", PostMessage)
            .WithName("PostMessage")
            .WithSummary("Stores the message and starts the run that answers it. Refused with 409 while a run is active in the thread.");
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

    private static async Task<Results<Accepted<SendMessageResponse>, NotFound, ProblemHttpResult>> PostMessage(
        Guid threadId,
        SendMessageRequest request,
        HttpContext context,
        RunService runs)
    {
        var access = context.GetSpaceAccess();
        var post = await runs.PostMessage(threadId, request.Text ?? string.Empty, access.UserId, context.RequestAborted);

        return post.Problem switch
        {
            PostProblem.NotFound => TypedResults.NotFound(),
            PostProblem.Invalid => TypedResults.Problem(title: post.Detail, statusCode: StatusCodes.Status400BadRequest),
            PostProblem.Busy => TypedResults.Problem(title: post.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Accepted(
                $"/api/spaces/{access.SpaceId}/runs/{post.Run!.Id}",
                new SendMessageResponse(post.Message!.ToResponse(), post.Run.ToSummary())),
        };
    }

    #endregion
}
