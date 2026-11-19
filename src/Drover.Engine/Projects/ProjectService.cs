namespace Drover.Engine.Projects;

/// <summary>A project change, or the plain reason it was refused.</summary>
public sealed record ProjectChange(Project? Project, string? Problem = null)
{
    public static readonly ProjectChange NotFound = new(Project: null);
}

/// <summary>The result of moving a conversation into or out of a project.</summary>
public enum ConversationProjectChange
{
    Changed,
    NotFound,
    NotOwner,
}

/// <summary>Project definitions and conversation membership inside a space.</summary>
public sealed class ProjectService(ProjectRepository projects, AuditLog audit)
{
    #region Properties

    public const int MaxNameLength = Project.MaxNameLength;

    public const int MaxInstructionsLength = Project.MaxInstructionsLength;

    #endregion

    #region Public Methods

    public async Task<List<ProjectResponse>> List(CancellationToken cancellationToken)
    {
        var found = await projects.List(cancellationToken);
        var conversations = (await projects.ListVisibleConversationProjects(cancellationToken))
            .ToLookup(membership => membership.ProjectId, membership => membership.ConversationId);
        return
        [
            .. found.Select(project => new ProjectResponse(
                project.Id,
                project.Name,
                project.Instructions,
                [.. conversations[project.Id].Order()])),
        ];
    }

    public async Task<ProjectResponse> Response(Project project, CancellationToken cancellationToken) =>
        new(
            project.Id,
            project.Name,
            project.Instructions,
            await projects.ListVisibleConversationIds(project.Id, cancellationToken));

    public ProjectChange Create(ProjectBody body, Guid actorUserId)
    {
        if (Problem(body) is { } problem)
        {
            return new(null, problem);
        }

        var project = new Project { Name = body.Name!.Trim(), Instructions = body.Instructions ?? string.Empty };
        projects.Add(project);
        audit.Record(actorUserId, "project.created", AuditTargets.Project, project.Id, null, null);
        return new(project);
    }

    public async Task<ProjectChange> Update(
        Guid projectId,
        ProjectBody body,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (Problem(body) is { } problem)
        {
            return new(null, problem);
        }

        if (await projects.GetForUpdate(projectId, cancellationToken) is not { } project)
        {
            return ProjectChange.NotFound;
        }

        JsonArray fields = [];
        var name = body.Name!.Trim();
        var instructions = body.Instructions ?? string.Empty;

        if (project.Name != name)
        {
            fields.Add("name");
        }

        if (project.Instructions != instructions)
        {
            fields.Add("instructions");
        }

        project.Name = name;
        project.Instructions = instructions;

        if (fields.Count > 0)
        {
            audit.Record(
                actorUserId,
                "project.updated",
                AuditTargets.Project,
                project.Id,
                null,
                null,
                new JsonObject { ["fields"] = fields });
        }

        return new(project);
    }

    public async Task<bool> Remove(Guid projectId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await projects.GetForUpdate(projectId, cancellationToken) is not { } project)
        {
            return false;
        }

        projects.Remove(project);
        audit.Record(actorUserId, "project.removed", AuditTargets.Project, project.Id, null, null);
        return true;
    }

    public async Task<ConversationProjectChange> SetConversationProject(
        Guid conversationId,
        Guid? projectId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (projectId is { } targetId && await projects.GetForUpdate(targetId, cancellationToken) is null)
        {
            return ConversationProjectChange.NotFound;
        }

        if (await projects.GetConversationForUpdate(conversationId, cancellationToken) is not { } conversation)
        {
            return ConversationProjectChange.NotFound;
        }

        if (conversation.OwnerUserId != actorUserId)
        {
            return ConversationProjectChange.NotOwner;
        }

        if (conversation.ProjectId != projectId)
        {
            var previous = conversation.ProjectId;
            if (await projects.SetConversationProject(conversation.Id, projectId, cancellationToken) != 1)
            {
                return ConversationProjectChange.NotOwner;
            }

            if ((projectId ?? previous) is { } auditProjectId)
            {
                audit.Record(
                    actorUserId,
                    "conversation.project_changed",
                    AuditTargets.Project,
                    auditProjectId,
                    null,
                    null,
                    new JsonObject
                    {
                        ["previous_project_id"] = previous,
                        ["project_id"] = projectId,
                    });
            }
        }

        return ConversationProjectChange.Changed;
    }

    #endregion

    #region Validation

    private static string? Problem(ProjectBody body) =>
        string.IsNullOrWhiteSpace(body.Name) || body.Name.Trim().Length > MaxNameLength
            ? $"Give the project a name of up to {MaxNameLength} characters."
            : (body.Instructions?.Length ?? 0) > MaxInstructionsLength
                ? $"Keep the project instructions under {MaxInstructionsLength} characters."
                : null;

    #endregion
}
