namespace Drover.Server.Extensions;

/// <summary>
/// Extensions of a space, under the tenant scope filter. Any member may see what is installed; an admin reads a
/// file and imports it, because importing makes a bot.
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
        return space;
    }

    #endregion

    #region Handlers

    private static async Task<Ok<List<ExtensionSummary>>> ListExtensions(HttpContext context, ExtensionService extensions) =>
        TypedResults.Ok((await extensions.ListInstalled(context.RequestAborted)).Select(installed => installed.ToSummary()).ToList());

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
