namespace Drover.Data.Repositories;

/// <summary>A visible conversation's project membership.</summary>
public sealed record ConversationProject(Guid ConversationId, Guid ProjectId);

/// <summary>Projects and conversation membership inside the caller's tenant scope.</summary>
public sealed class ProjectRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(Project project) => db.Projects.Add(project);

    public Task<List<Project>> List(CancellationToken cancellationToken) =>
        db.Projects.AsNoTracking().OrderBy(project => project.Name).ThenBy(project => project.Id).ToListAsync(cancellationToken);

    public Task<List<ConversationProject>> ListVisibleConversationProjects(CancellationToken cancellationToken) =>
        db.Threads.AsNoTracking()
            .Where(thread => thread.ProjectId != null)
            .Select(thread => new ConversationProject(thread.Id, thread.ProjectId!.Value))
            .ToListAsync(cancellationToken);

    public Task<List<Guid>> ListVisibleConversationIds(Guid projectId, CancellationToken cancellationToken) =>
        db.Threads.AsNoTracking()
            .Where(thread => thread.ProjectId == projectId)
            .OrderBy(thread => thread.Id)
            .Select(thread => thread.Id)
            .ToListAsync(cancellationToken);

    public async Task<Project?> GetForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.Projects.FromSql($"SELECT * FROM projects WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken))
            .SingleOrDefault();

    public async Task<ChatThread?> GetConversationForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.Threads.FromSql($"SELECT * FROM threads WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken))
            .SingleOrDefault();

    public Task<int> SetConversationProject(
        Guid conversationId,
        Guid? projectId,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE owned_thread_projects SET project_id = {projectId} WHERE id = {conversationId}",
            cancellationToken);

    public void Remove(Project project) => db.Projects.Remove(project);

    #endregion
}
