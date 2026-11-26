namespace Drover.Server.Extensions;

/// <summary>
/// Extensions of a space. Any member may see what is installed and the themes it offers; an admin reads a file,
/// imports it, connects its tool server, and removes it.
/// </summary>
public static class ExtensionEndpoints
{
    #region Private Fields

    // The file travels as a JSON string, which can double its size, with a little room for the rest of the request.
    private const int MaxRequestBytes = (ExtensionPackages.MaxPackageBytes * 2) + 4096;

    private static readonly string tooLarge = $"This file is larger than an extension can be ({ExtensionPackages.MaxPackageBytes / 1024} KB).";

    #endregion

    #region Public Methods

    public static IEndpointRouteBuilder MapExtensionEndpoints(this RouteGroupBuilder space)
    {
        var extensions = space.MapGroup("/extensions").AddEndpointFilter<TenantScopeFilter>();
        extensions.MapGet("/", ListExtensions).WithName("ListExtensions");
        extensions.MapPost("/preview", PreviewExtension).RequireSpaceRole(MembershipRole.Admin).WithName("PreviewExtension")
            .WithBodyLimit(MaxRequestBytes, _ => tooLarge);
        extensions.MapPost("/", ImportExtension).RequireSpaceRole(MembershipRole.Admin).WithName("ImportExtension")
            .WithBodyLimit(MaxRequestBytes, _ => tooLarge);

        space.MapGet("/themes", ListThemes).AddEndpointFilter<TenantScopeFilter>().WithName("ListSpaceThemes")
            .WithSummary("The themes the space's installed extensions offer its members, each as a theme file.");

        // Connecting waits on the tool server, and removing takes its connection away, so both own their tenant scopes.
        space.MapPost("/extensions/{extensionId:guid}/connection", ConnectExtension).RequireSpaceRole(MembershipRole.Admin).WithName("ConnectExtension")
            .WithSummary("Connects the tool server an installed extension names. A secret setting or header value goes to the vault and is never returned.");
        space.MapDelete("/extensions/{extensionId:guid}", RemoveExtension).RequireSpaceRole(MembershipRole.Admin).WithName("RemoveExtension")
            .WithSummary("Removes an extension, with the tool connection it made. A rule already added to a bot stays.");
        return space;
    }

    #endregion

    #region Handlers

    private static async Task<Ok<List<ExtensionSummary>>> ListExtensions(HttpContext context, ExtensionService extensions) =>
        TypedResults.Ok((await extensions.ListInstalled(context.RequestAborted)).Select(installed => installed.ToSummary()).ToList());

    private static async Task<Ok<List<SpaceTheme>>> ListThemes(HttpContext context, ExtensionService extensions) =>
        TypedResults.Ok(await extensions.ListThemes(context.RequestAborted));

    private static async Task<Results<Ok<ExtensionPreview>, ProblemHttpResult>> PreviewExtension(
        ExtensionText request,
        HttpContext context,
        ExtensionService extensions)
    {
        var read = await extensions.Preview(request.Package, context.RequestAborted);
        return read.Value is { } preview
            ? TypedResults.Ok(preview)
            : TypedResults.Problem(title: read.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ExtensionSummary>, NotFound, ProblemHttpResult>> ConnectExtension(
        Guid extensionId,
        ConnectExtensionRequest request,
        HttpContext context,
        ExtensionConnector connector)
    {
        var connected = await connector.Connect(extensionId, request, context.GetSpaceAccess().UserId, context.RequestAborted);
        return connected switch
        {
            { State: { } state } => TypedResults.Ok(state.ToSummary()),
            { Problem: { } problem } => TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound>> RemoveExtension(Guid extensionId, HttpContext context, ExtensionConnector connector) =>
        await connector.Remove(extensionId, context.GetSpaceAccess().UserId, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<Created<ExtensionSummary>, ProblemHttpResult>> ImportExtension(
        ExtensionText request,
        HttpContext context,
        ExtensionService extensions)
    {
        var access = context.GetSpaceAccess();
        var imported = await extensions.Import(request.Package, access.UserId, context.RequestAborted);
        return imported.Value is { } installed
            ? TypedResults.Created($"/api/spaces/{access.SpaceId}/extensions/{installed.Install.Id}", installed.ToSummary())
            : TypedResults.Problem(title: imported.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    #endregion
}
