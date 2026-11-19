namespace Drover.Server.Projects;

/// <summary>Shared project definitions and conversation membership inside one space.</summary>
public static class ProjectEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapProjectEndpoints(this RouteGroupBuilder space)
    {
        var projects = space.MapGroup("/projects").AddEndpointFilter<TenantScopeFilter>();
        projects.MapGet("/", ListProjects).WithName("ListProjects");
        projects.MapPost("/", CreateProject).RequireSpaceRole(MembershipRole.Admin).WithName("CreateProject");
        projects.MapPut("/{projectId:guid}", UpdateProject).RequireSpaceRole(MembershipRole.Admin).WithName("UpdateProject");
        space.MapDelete("/projects/{projectId:guid}", RemoveProject)
            .RequireSpaceRole(MembershipRole.Admin).WithName("RemoveProject");

        space.MapPut("/threads/{conversationId:guid}/project", SetConversationProject)
            .AddEndpointFilter<TenantScopeFilter>()
            .WithName("SetConversationProject");
        return space;
    }

    #endregion

    #region Handlers

    private static async Task<Ok<List<ProjectResponse>>> ListProjects(HttpContext context, ProjectService projects) =>
        TypedResults.Ok(await projects.List(context.RequestAborted));

    private static Results<Created<ProjectResponse>, ProblemHttpResult> CreateProject(
        ProjectBody body,
        HttpContext context,
        ProjectService projects)
    {
        var access = context.GetSpaceAccess();
        var change = projects.Create(body, access.UserId);
        return change.Project is { } project
            ? TypedResults.Created(
                $"/api/spaces/{access.SpaceId}/projects/{project.Id}",
                new ProjectResponse(project.Id, project.Name, project.Instructions, []))
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ProjectResponse>, NotFound, ProblemHttpResult>> UpdateProject(
        Guid projectId,
        ProjectBody body,
        HttpContext context,
        ProjectService projects)
    {
        var change = await projects.Update(projectId, body, context.GetSpaceAccess().UserId, context.RequestAborted);
        return change switch
        {
            { Problem: { } problem } => TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest),
            { Project: { } project } => TypedResults.Ok(await projects.Response(project, context.RequestAborted)),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound>> RemoveProject(
        Guid projectId,
        HttpContext context,
        ProjectService projects,
        FileService files) =>
        await files.RemoveProject(projectId, context.GetSpaceAccess().UserId, projects, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> SetConversationProject(
        Guid conversationId,
        SetConversationProjectRequest request,
        HttpContext context,
        ProjectService projects) =>
        await projects.SetConversationProject(
            conversationId,
            request.ProjectId,
            context.GetSpaceAccess().UserId,
            context.RequestAborted) switch
        {
            ConversationProjectChange.Changed => TypedResults.NoContent(),
            ConversationProjectChange.NotOwner => TypedResults.Forbid(),
            _ => TypedResults.NotFound(),
        };

    #endregion
}
