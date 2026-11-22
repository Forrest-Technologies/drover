namespace Drover.Server.Sandboxes;

/// <summary>
/// Sandbox secrets of a space: credentials bots use in their sandboxes. Owners and admins manage them; a member may
/// read only the names a bot's sandbox is given. No route returns a value, its length, or anything derived from it.
/// A value arrives once, on create or rotate, and is written to the vault.
/// </summary>
public static class SandboxSecretEndpoints
{
    #region Private Fields

    // A value is at most 16,384 characters; escaped as JSON (six bytes a character at worst) with every other field.
    private const long MaxBodyBytes = 128 * 1024;

    // A change of scope names at most 200 bots; everything else here is a few short fields.
    private const long MaxChangeBytes = 16 * 1024;

    #endregion

    #region Public Methods

    public static IEndpointRouteBuilder MapSandboxSecretEndpoints(this RouteGroupBuilder space)
    {
        var secrets = space.MapGroup("/sandbox-secrets")
            .AddEndpointFilter<TenantScopeFilter>()
            .RequireSpaceRole(MembershipRole.Admin);
        secrets.MapGet("/", List).WithName("ListSandboxSecrets");
        secrets.MapPost("/", Create).WithBodyLimit(MaxBodyBytes).WithName("CreateSandboxSecret");
        secrets.MapPut("/{secretId:guid}/value", Rotate).WithBodyLimit(MaxBodyBytes).WithName("RotateSandboxSecret");
        secrets.MapPut("/{secretId:guid}/scope", ChangeScope).WithBodyLimit(MaxChangeBytes).WithName("ChangeSandboxSecretScope");
        secrets.MapPut("/{secretId:guid}/binding", ChangeBinding).WithBodyLimit(MaxChangeBytes).WithName("ChangeSandboxSecretBinding");
        secrets.MapPut("/{secretId:guid}/description", ChangeDescription).WithBodyLimit(MaxChangeBytes).WithName("ChangeSandboxSecretDescription");
        secrets.MapDelete("/{secretId:guid}", Delete).WithName("DeleteSandboxSecret");
        secrets.MapPost("/{secretId:guid}/restart-sandboxes", RestartSandboxes)
            .WithName("RestartSandboxSecretSandboxes")
            .WithSummary("Takes out of use the live sandboxes of every bot the secret reaches, so the next use starts one with the secrets as they are now.");
        secrets.MapGet("/{secretId:guid}/history", History).WithName("GetSandboxSecretHistory");

        space.MapGet("/bots/{botId:guid}/sandbox/secrets", NamesForBot)
            .AddEndpointFilter<TenantScopeFilter>()
            .WithName("ListBotSandboxSecretNames")
            .WithSummary("The secret names a bot's sandbox is given, without values, and where each comes from.");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Ok<List<SandboxSecretResponse>>> List(HttpContext context, SandboxSecretService secrets) =>
        TypedResults.Ok(await secrets.List(context.RequestAborted));

    private static async Task<Results<Created<SandboxSecretResponse>, ProblemHttpResult>> Create(
        CreateSandboxSecretRequest request,
        HttpContext context,
        SandboxSecretService secrets)
    {
        var access = context.GetSpaceAccess();
        var change = await secrets.Create(
            request with
            {
                Name = request.Name ?? string.Empty,
                Value = request.Value ?? string.Empty,
                Mode = request.Mode ?? string.Empty,
                Scope = request.Scope ?? string.Empty,
            },
            access.UserId,
            context.RequestAborted);

        return change.Secret is { } secret
            ? TypedResults.Created($"/api/spaces/{access.SpaceId}/sandbox-secrets/{secret.Id}", secret)
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<SandboxSecretResponse>, NotFound, ProblemHttpResult>> Rotate(
        Guid secretId,
        RotateSandboxSecretRequest request,
        HttpContext context,
        SandboxSecretService secrets) =>
        Answer(await secrets.Rotate(
            secretId,
            request with { Value = request.Value ?? string.Empty },
            context.GetSpaceAccess().UserId,
            context.RequestAborted));

    private static async Task<Results<Ok<SandboxSecretResponse>, NotFound, ProblemHttpResult>> ChangeScope(
        Guid secretId,
        ChangeSandboxSecretScopeRequest request,
        HttpContext context,
        SandboxSecretService secrets) =>
        Answer(await secrets.ChangeScope(secretId, request, context.GetSpaceAccess().UserId, context.RequestAborted));

    private static async Task<Results<Ok<SandboxSecretResponse>, NotFound, ProblemHttpResult>> ChangeBinding(
        Guid secretId,
        ChangeSandboxSecretBindingRequest request,
        HttpContext context,
        SandboxSecretService secrets) =>
        Answer(await secrets.ChangeBinding(secretId, request, context.GetSpaceAccess().UserId, context.RequestAborted));

    private static async Task<Results<Ok<SandboxSecretResponse>, NotFound, ProblemHttpResult>> ChangeDescription(
        Guid secretId,
        ChangeSandboxSecretDescriptionRequest request,
        HttpContext context,
        SandboxSecretService secrets) =>
        Answer(await secrets.ChangeDescription(secretId, request, context.GetSpaceAccess().UserId, context.RequestAborted));

    private static async Task<Results<NoContent, NotFound>> Delete(Guid secretId, HttpContext context, SandboxSecretService secrets) =>
        await secrets.Delete(secretId, context.GetSpaceAccess().UserId, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<Ok<SandboxSandboxesRestarted>, NotFound>> RestartSandboxes(
        Guid secretId,
        HttpContext context,
        SandboxSecretService secrets) =>
        await secrets.RestartSandboxes(secretId, context.GetSpaceAccess().UserId, context.RequestAborted) is { } count
            ? TypedResults.Ok(new SandboxSandboxesRestarted(count))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<List<SandboxSecretHistoryEntry>>, NotFound>> History(
        Guid secretId,
        HttpContext context,
        SandboxSecretService secrets) =>
        await secrets.History(secretId, context.RequestAborted) is { } history
            ? TypedResults.Ok(history)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<List<BotSandboxSecretName>>, NotFound>> NamesForBot(
        Guid botId,
        HttpContext context,
        SandboxSecretService secrets) =>
        await secrets.NamesForBot(botId, context.RequestAborted) is { } names
            ? TypedResults.Ok(names)
            : TypedResults.NotFound();

    #endregion

    #region Private Methods

    private static Results<Ok<SandboxSecretResponse>, NotFound, ProblemHttpResult> Answer(SandboxSecretChange change)
    {
        if (change.Secret is { } secret)
        {
            return TypedResults.Ok(secret);
        }

        return change.Problem is null
            ? TypedResults.NotFound()
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    #endregion
}
