namespace Drover.Server.Spaces;

public static class SpaceEndpoints
{
    #region Public Methods

    /// <summary>
    /// Everything under <c>/api/spaces/{spaceId}</c>. The group requires a signed-in person; the membership
    /// middleware confirms they belong to the space; the filter then opens the tenant transaction, except on endpoints that wait on the network.
    /// </summary>
    public static IEndpointRouteBuilder MapSpaceEndpoints(this RouteGroupBuilder api)
    {
        var root = api.MapGroup("/spaces/{spaceId:guid}")
            .RequireAuthorization()
            .WithMetadata(new SpaceScopedMetadata());
        var space = root.MapGroup(string.Empty).AddEndpointFilter<TenantScopeFilter>();

        space.MapGet("/", GetSpace).WithName("GetSpace");
        space.MapGet("/secrets", ListSecrets).RequireSpaceRole(MembershipRole.Admin).WithName("ListSecrets");
        root.MapStatusEndpoints();

        // Model connections choose their own filter: testing one waits on the provider and must not hold a scope.
        root.MapModelConnectionEndpoints();
        root.MapToolConnectionEndpoints();

        // Bots, threads, and runs choose their own filter too: posting a message and cancelling a run commit first
        // and then wake the workers.
        root.MapBotEndpoints();
        root.MapThreadEndpoints();
        root.MapRunEndpoints();
        root.MapRunStreamEndpoints();
        return api;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<SpaceResponse>, NotFound>> GetSpace(
        HttpContext context,
        SpaceService spaces)
    {
        var access = context.GetSpaceAccess();
        var space = await spaces.GetSpace(access.SpaceId, context.RequestAborted);

        if (space is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new SpaceResponse(
            space.Id,
            space.Kind.ToString().ToLowerInvariant(),
            space.Name,
            space.Slug,
            access.Role.ToString().ToLowerInvariant()));
    }

    /// <summary>Metadata only. The value of a secret is never returned by any endpoint.</summary>
    private static async Task<Ok<List<SecretSummary>>> ListSecrets(HttpContext context, SpaceService spaces)
    {
        var secrets = await spaces.ListSecrets(context.RequestAborted);
        return TypedResults.Ok(secrets
            .Select(secret => new SecretSummary(
                secret.Id,
                secret.Kind.ToString(),
                HasValue: true,
                IsPrivate: secret.AudienceUserId != Audience.Everyone,
                secret.CreatedAt,
                secret.LastUsedAt))
            .ToList());
    }

    #endregion
}
