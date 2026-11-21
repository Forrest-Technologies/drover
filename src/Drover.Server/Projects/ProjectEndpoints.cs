namespace Drover.Server.Projects;

/// <summary>
/// Projects of a space, under the tenant scope filter. Every member may list them and read their conversations; owners
/// and admins create, rename, edit instructions, and remove them.
/// </summary>
public static class ProjectEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapProjectEndpoints(this RouteGroupBuilder space)
    {
        var projects = space.MapGroup("/projects").AddEndpointFilter<TenantScopeFilter>();
        projects.MapGet("/", ListProjects).WithName("ListProjects");
        projects.MapGet("/{projectId:guid}", GetProject).WithName("GetProject");
        projects.MapGet("/{projectId:guid}/threads", ListThreads).WithName("ListProjectThreads");
        projects.MapPost("/", CreateProject).RequireSpaceRole(MembershipRole.Admin).WithName("CreateProject");
        projects.MapPut("/{projectId:guid}", UpdateProject).RequireSpaceRole(MembershipRole.Admin).WithName("UpdateProject");
        space.MapDelete("/projects/{projectId:guid}", RemoveProject).RequireSpaceRole(MembershipRole.Admin).WithName("RemoveProject");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Ok<List<ProjectSummary>>> ListProjects(HttpContext context, ProjectService projects) =>
        TypedResults.Ok((await projects.ListProjects(context.RequestAborted)).Select(project => project.ToSummary()).ToList());

    private static async Task<Results<Ok<ProjectSummary>, NotFound>> GetProject(
        Guid projectId,
        HttpContext context,
        ProjectService projects) =>
        await projects.FindProject(projectId, context.RequestAborted) is { } project
            ? TypedResults.Ok(project.ToSummary())
            : TypedResults.NotFound();

    private static async Task<Results<Ok<List<ThreadSummary>>, NotFound>> ListThreads(
        Guid projectId,
        HttpContext context,
        ThreadService threads)
    {
        var found = await threads.ListThreadsForProject(projectId, context.GetSpaceAccess().UserId, context.RequestAborted);
        return found is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(found.Select(view => view.Thread.ToSummary(view.ActiveRunId, view.Attention)).ToList());
    }

    private static async Task<Results<Created<ProjectSummary>, ProblemHttpResult>> CreateProject(
        CreateProjectRequest request,
        HttpContext context,
        ProjectService projects)
    {
        var access = context.GetSpaceAccess();
        var change = await projects.Create(
            request.Name,
            request.Instructions,
            AuditActor.Person(access.UserId),
            context.RequestAborted);

        return change.Project is { } project
            ? TypedResults.Created($"/api/spaces/{access.SpaceId}/projects/{project.Id}", project.ToSummary())
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ProjectSummary>, NotFound, ProblemHttpResult>> UpdateProject(
        Guid projectId,
        UpdateProjectRequest request,
        HttpContext context,
        ProjectService projects)
    {
        var access = context.GetSpaceAccess();
        var change = await projects.Update(
            projectId,
            request.Name,
            request.Instructions,
            AuditActor.Person(access.UserId),
            context.RequestAborted);

        return change switch
        {
            { Project: { } project } => TypedResults.Ok(project.ToSummary()),
            { Problem: { } problem } => TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound>> RemoveProject(
        Guid projectId,
        HttpContext context,
        ProjectService projects)
    {
        var access = context.GetSpaceAccess();
        return await projects.Remove(projectId, AuditActor.Person(access.UserId), context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    #endregion
}
