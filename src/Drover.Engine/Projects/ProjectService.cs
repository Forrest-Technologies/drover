namespace Drover.Engine.Projects;

/// <summary>The outcome of creating or editing a project: the stored row, or why it was refused. Neither when not found.</summary>
public sealed record ProjectChange(Project? Project, string? Problem = null)
{
    #region Properties

    public static ProjectChange NotFound => new(null, null);

    #endregion
}

/// <summary>
/// Projects of the current space: reading them, changing their name and shared instructions, and removing them while
/// leaving every conversation intact. Each change is checked here and recorded in the audit log without writing
/// instruction text. Listing, reading, and editing run in the caller's tenant scope; removal opens its own scope so
/// project files are deleted from the file store after commit.
/// </summary>
public sealed class ProjectService(
    TenantScope scope,
    TenantContext tenant,
    ProjectRepository projects,
    FileRepository files,
    IFileStore store,
    AuditLog audit,
    TimeProvider time,
    ILogger<ProjectService> logger)
{
    #region Public Methods

    public Task<List<Project>> ListProjects(CancellationToken cancellationToken) =>
        projects.ListProjects(cancellationToken);

    public Task<Project?> FindProject(Guid id, CancellationToken cancellationToken) =>
        projects.FindProject(id, cancellationToken);

    public async Task<ProjectChange> Create(
        string? name,
        string? instructions,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        var instructionsText = instructions ?? string.Empty;

        if (FindProblem(name, instructionsText) is { } problem)
        {
            return new ProjectChange(null, problem);
        }

        var now = time.GetUtcNow();
        var project = new Project
        {
            Name = name!.Trim(),
            Instructions = instructionsText,
            CreatedAt = now,
            UpdatedAt = now,
        };
        projects.Add(project);
        audit.Record(actor, "project.created", AuditTargets.Project, project.Id, null, Snapshot(project));
        logger.LogInformation("Project {ProjectId} created", project.Id);
        return new ProjectChange(project);
    }

    public async Task<ProjectChange> Update(
        Guid id,
        string? name,
        string? instructions,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        if (await projects.GetProjectForUpdate(id, cancellationToken) is not { } project)
        {
            return ProjectChange.NotFound;
        }

        var nextName = name ?? project.Name;
        var nextInstructions = instructions ?? project.Instructions;

        if (FindProblem(nextName, nextInstructions) is { } problem)
        {
            return new ProjectChange(null, problem);
        }

        var before = Snapshot(project);
        project.Name = nextName.Trim();
        project.Instructions = nextInstructions;

        if (audit.Record(actor, "project.updated", AuditTargets.Project, project.Id, before, Snapshot(project)))
        {
            project.UpdatedAt = time.GetUtcNow();
            logger.LogInformation("Project {ProjectId} updated", project.Id);
        }

        return new ProjectChange(project);
    }

    /// <summary>
    /// Removes the project, detaches every conversation in the same transaction so conversations stay intact, removes
    /// its shared project files and releases their quota once, and deletes their stored bytes after commit.
    /// </summary>
    public async Task<bool> Remove(Guid id, AuditActor actor, CancellationToken cancellationToken)
    {
        var spaceId = tenant.SpaceId ?? throw new InvalidOperationException("Projects are always handled inside a space.");
        List<StoredFile> projectFiles;

        await using (var work = await scope.Begin(cancellationToken))
        {
            if (await projects.GetProjectForUpdate(id, cancellationToken) is not { } project)
            {
                return false;
            }

            projectFiles = await files.GetProjectFilesForUpdate(project.Id, cancellationToken);
            var released = projectFiles.Sum(file => file.Size);

            if (projectFiles.Count > 0)
            {
                files.RemoveRange(projectFiles);
            }

            if (released > 0)
            {
                await files.ReleaseBytes(spaceId, released, cancellationToken);
            }

            await projects.ClearThreadsForProject(project.Id, cancellationToken);
            projects.Remove(project);
            audit.Record(actor, "project.removed", AuditTargets.Project, project.Id, Snapshot(project), null);
            await work.Commit(cancellationToken);
        }

        foreach (var file in projectFiles)
        {
            await store.Delete(spaceId, file.StorageKey, CancellationToken.None);
        }

        logger.LogInformation("Project {ProjectId} removed", id);
        return true;
    }

    #endregion

    #region Private Methods

    private static string? FindProblem(string? name, string instructions)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > Project.MaxNameLength)
        {
            return $"Give the project a name of up to {Project.MaxNameLength} characters.";
        }

        return instructions.Length > Project.MaxInstructionsLength
            ? $"Keep the instructions under {Project.MaxInstructionsLength} characters."
            : null;
    }

    // Never write project instructions into the append-only audit log: record length and hash instead.
    private static JsonObject Snapshot(Project project) =>
        AuditLog.Snapshot(new
        {
            project.Name,
            InstructionsLength = project.Instructions.Length,
            InstructionsSha256 = Sha256Hex(project.Instructions),
        });

    private static string Sha256Hex(string text)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(text), digest);
        return Convert.ToHexStringLower(digest);
    }

    #endregion
}
