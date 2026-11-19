namespace Drover.Data.Repositories;

/// <summary>
/// Projects as rows. Space isolation shows a caller only their space's projects. Runs inside the caller's tenant
/// scope.
/// </summary>
public sealed class ProjectRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(Project project) => db.Projects.Add(project);

    public void Remove(Project project) => db.Projects.Remove(project);

    public Task<List<Project>> ListProjects(CancellationToken cancellationToken) =>
        db.Projects.AsNoTracking()
            .OrderByDescending(project => project.UpdatedAt)
            .ThenByDescending(project => project.Id)
            .ToListAsync(cancellationToken);

    public Task<Project?> FindProject(Guid id, CancellationToken cancellationToken) =>
        db.Projects.AsNoTracking().SingleOrDefaultAsync(project => project.Id == id, cancellationToken);

    /// <summary>The tracked row, locked until the transaction ends, so edits and removal wait their turn.</summary>
    public async Task<Project?> GetProjectForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.Projects.FromSql($"SELECT * FROM projects WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken))
            .SingleOrDefault();

    /// <summary>
    /// Confirms the project exists in this space and holds a shared key lock until commit, so a concurrent removal
    /// waits rather than racing a conversation into a deleted project.
    /// </summary>
    public Task<Project?> LockProjectForShare(Guid id, CancellationToken cancellationToken) =>
        db.Projects.FromSql($"SELECT * FROM projects WHERE id = {id} FOR KEY SHARE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Clears <c>project_id</c> on every conversation in the project within this space, including private
    /// conversations the caller cannot read, through an audited system function so conversations stay intact when the
    /// project is deleted.
    /// </summary>
    public Task<int> ClearThreadsForProject(Guid projectId, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<int>($"SELECT sys.clear_project_threads({projectId}) AS \"Value\"")
            .SingleAsync(cancellationToken);

    #endregion
}
