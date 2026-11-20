namespace Drover.Contracts.Files;

/// <summary>
/// A file shared in a project: the stored file's details, the project it belongs to, and when it was uploaded.
/// </summary>
public sealed record ProjectFileResponse(
    FileResponse File,
    Guid ProjectId,
    DateTimeOffset UploadedAt);
