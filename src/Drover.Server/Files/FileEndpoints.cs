namespace Drover.Server.Files;

/// <summary>
/// Files a person attaches to a message. Uploading and downloading move bytes over the network, so they sit outside
/// the tenant scope filter and the service opens short scopes of its own. A download is authorized by the file's row,
/// which row-level security shows only to people who can see its conversation, never by a key; and it is served so a
/// browser cannot run it in Drover's origin: a fixed content type for the kinds Drover shows, attachment otherwise,
/// and a policy that sandboxes the response and lets it load nothing.
/// </summary>
public static class FileEndpoints
{
    #region Private Fields

    private const string DownloadPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";

    #endregion

    #region Public Methods

    public static IEndpointRouteBuilder MapFileEndpoints(this RouteGroupBuilder space)
    {
        var rules = ((IEndpointRouteBuilder)space).ServiceProvider.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules;

        space.MapGet("/threads/{threadId:guid}/attachment-rules", GetAttachmentRules)
            .AddEndpointFilter<TenantScopeFilter>()
            .WithName("GetAttachmentRules")
            .WithSummary("What the thread's bot can take: the server's file limits, whether its model reads pictures, and whether it has a sandbox.");
        space.MapPost("/threads/{threadId:guid}/files", Upload)
            .WithBodyLimit(rules.MaxFileBytes, declared => FileWords.TooLarge(declared, rules.MaxFileBytes))
            .WithName("UploadFile")
            .WithSummary("Stores the request body as a file for a message the caller has not sent yet. The name is in the query. A file past a limit, or one the bot cannot open, is refused with a sentence and a refusal code.");
        space.MapGet("/files/{fileId:guid}", Download)
            .WithName("DownloadFile")
            .WithSummary("The file's content, for someone who can see its conversation. download=true asks for it as an attachment.");
        space.MapGet("/bots/{botId:guid}/files", ListExchanged)
            .AddEndpointFilter<TenantScopeFilter>()
            .WithName("ListExchangedFiles")
            .WithSummary("The files the caller and the bot exchanged in the caller's conversations with it, newest first: the ones the bot shared and the ones the caller sent.");
        space.MapDelete("/files/{fileId:guid}", RemovePending)
            .WithName("RemovePendingFile")
            .WithSummary("Removes a file the caller attached and has not sent. A sent file is kept as long as its message.");
        var project = space.MapGroup("/projects/{projectId:guid}/files");
        project.MapGet("/", ListProject).WithName("ListProjectFiles");
        project.MapGet("/{fileId:guid}", DownloadProject).WithName("DownloadProjectFile");
        project.MapPost("/", UploadProject)
            .RequireSpaceRole(MembershipRole.Admin)
            .WithBodyLimit(rules.MaxFileBytes, declared => FileWords.TooLarge(declared, rules.MaxFileBytes))
            .WithName("UploadProjectFile");
        project.MapDelete("/{fileId:guid}", RemoveProjectFile)
            .RequireSpaceRole(MembershipRole.Admin)
            .WithName("RemoveProjectFile");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<AttachmentRules>, NotFound>> GetAttachmentRules(Guid threadId, HttpContext context, FileService files) =>
        await files.GetRules(threadId, context.RequestAborted) is { } rules ? TypedResults.Ok(rules) : TypedResults.NotFound();

    private static async Task<Results<Created<FileResponse>, NotFound, ProblemHttpResult>> Upload(
        Guid threadId,
        string? name,
        HttpContext context,
        FileService files)
    {
        var access = context.GetSpaceAccess();
        var upload = await files.Upload(threadId, name, context.Request.Body, context.Request.ContentLength, access.UserId, context.RequestAborted);

        return UploadResponse(upload, $"/api/spaces/{access.SpaceId}/files");
    }

    private static async Task<Results<Created<FileResponse>, NotFound, ProblemHttpResult>> UploadProject(
        Guid projectId, string? name, HttpContext context, FileService files)
    {
        var access = context.GetSpaceAccess();
        var upload = await files.UploadProject(
            projectId, name, context.Request.Body, context.Request.ContentLength, access.UserId,
            context.RequestAborted);
        return UploadResponse(upload, $"/api/spaces/{access.SpaceId}/projects/{projectId}/files");
    }

    private static Results<Created<FileResponse>, NotFound, ProblemHttpResult> UploadResponse(
        FileUpload upload, string collection) =>
        upload switch
        {
            { File: { } file } => TypedResults.Created($"{collection}/{file.Id}", file.ToResponse()),
            { Problem: FileProblem.NotFound } => TypedResults.NotFound(),
            _ => TypedResults.Problem(
                title: upload.Detail,
                statusCode: upload.Refusal is FileRefusals.TooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["refusal"] = upload.Refusal }),
        };

    private static async Task<IResult> Download(Guid fileId, bool? download, HttpContext context, FileService files) =>
        Serve(await files.Open(fileId, context.RequestAborted), download, context);

    private static async Task<IResult> DownloadProject(
        Guid projectId, Guid fileId, bool? download, HttpContext context, FileService files) =>
        Serve(await files.OpenProject(projectId, fileId, context.RequestAborted), download, context);

    private static async Task<Results<Ok<List<FileResponse>>, NotFound>> ListProject(
        Guid projectId, HttpContext context, FileService files) =>
        await files.ListProject(projectId, context.RequestAborted) is { } found
            ? TypedResults.Ok<List<FileResponse>>([.. found.Select(file => file.ToResponse())])
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> RemoveProjectFile(
        Guid projectId, Guid fileId, HttpContext context, FileService files) =>
        await files.RemoveProjectFile(projectId, fileId, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static IResult Serve(FileDownload? found, bool? download, HttpContext context)
    {
        if (found is null)
        {
            return TypedResults.NotFound();
        }

        var (contentType, inline) = FileInspector.KindOf(found.File.ContentType) switch
        {
            FileKind.Image => (found.File.ContentType, download is not true),
            FileKind.Text => ("text/plain; charset=utf-8", download is not true),
            _ => ("application/octet-stream", false),
        };
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = DownloadPolicy;
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers.ContentDisposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment")
        {
            FileNameStar = found.File.Name,
        }.ToString();
        return TypedResults.Stream(found.Content, contentType);
    }

    private static async Task<Results<Ok<IReadOnlyList<ExchangedFileResponse>>, NotFound>> ListExchanged(Guid botId, HttpContext context, FileService files) =>
        await files.ListExchanged(botId, context.GetSpaceAccess().UserId, context.RequestAborted) is { } exchanged
            ? TypedResults.Ok<IReadOnlyList<ExchangedFileResponse>>([.. exchanged.Select(found => found.ToResponse())])
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> RemovePending(Guid fileId, HttpContext context, FileService files) =>
        await files.RemovePending(fileId, context.GetSpaceAccess().UserId, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    #endregion
}
