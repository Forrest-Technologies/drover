namespace Drover.Client;

/// <summary>Projects and conversation membership inside one space.</summary>
public sealed partial class DroverApi
{
    #region Public Methods

    public Task<List<ProjectResponse>> ListProjects(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<List<ProjectResponse>>($"{Space(spaceId)}/projects", cancellationToken);

    public Task<ProjectResponse> CreateProject(
        Guid spaceId,
        ProjectBody project,
        CancellationToken cancellationToken = default) =>
        Post<ProjectResponse>($"{Space(spaceId)}/projects", project, cancellationToken);

    public Task<ProjectResponse> UpdateProject(
        Guid spaceId,
        Guid projectId,
        ProjectBody project,
        CancellationToken cancellationToken = default) =>
        Send<ProjectResponse>(HttpMethod.Put, $"{Space(spaceId)}/projects/{projectId}", project, cancellationToken);

    public Task RemoveProject(Guid spaceId, Guid projectId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/projects/{projectId}", cancellationToken);

    public Task SetConversationProject(
        Guid spaceId,
        Guid conversationId,
        Guid? projectId,
        CancellationToken cancellationToken = default) =>
        SendForNothing(
            HttpMethod.Put,
            $"{Space(spaceId)}/threads/{conversationId}/project",
            new SetConversationProjectRequest(projectId),
            cancellationToken);

    #endregion
}
