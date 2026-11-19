namespace Drover.Client;

/// <summary>Projects of a space: listing, reading, creating, editing, and removing them.</summary>
public sealed partial class DroverApi
{
    #region Projects

    public Task<List<ProjectSummary>> ListProjects(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<List<ProjectSummary>>($"{Space(spaceId)}/projects", cancellationToken);

    public Task<ProjectSummary> GetProject(Guid spaceId, Guid projectId, CancellationToken cancellationToken = default) =>
        Get<ProjectSummary>($"{Space(spaceId)}/projects/{projectId}", cancellationToken);

    public Task<List<ThreadSummary>> ListProjectThreads(Guid spaceId, Guid projectId, CancellationToken cancellationToken = default) =>
        Get<List<ThreadSummary>>($"{Space(spaceId)}/projects/{projectId}/threads", cancellationToken);

    public Task<ProjectSummary> CreateProject(Guid spaceId, CreateProjectRequest request, CancellationToken cancellationToken = default) =>
        Post<ProjectSummary>($"{Space(spaceId)}/projects", request, cancellationToken);

    public Task<ProjectSummary> UpdateProject(Guid spaceId, Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default) =>
        Send<ProjectSummary>(HttpMethod.Put, $"{Space(spaceId)}/projects/{projectId}", request, cancellationToken);

    public Task RemoveProject(Guid spaceId, Guid projectId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/projects/{projectId}", cancellationToken);

    #endregion
}
