namespace Drover.Server.Spaces;

/// <summary>
/// For an endpoint under a space: reads the space from the route, confirms the signed-in person is a member, checks
/// the role the endpoint asks for, and only then sets the space on the tenant context. A person who is not a member
/// gets 404, the same as for a space that does not exist, and the check reads nothing but their own memberships.
/// </summary>
public sealed class SpaceMembershipMiddleware(RequestDelegate next)
{
    #region Public Methods

    public async Task Invoke(HttpContext context, AccountService accounts, TenantContext tenant)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint?.Metadata.GetMetadata<SpaceScopedMetadata>() is null)
        {
            await next(context);
            return;
        }

        var userId = context.User.GetUserId();

        if (userId == Guid.Empty || !TryReadSpaceId(context, out var spaceId))
        {
            await Refuse(context, StatusCodes.Status404NotFound);
            return;
        }

        var role = await accounts.FindRole(spaceId, userId, context.RequestAborted);

        if (role is not { } member)
        {
            await Refuse(context, StatusCodes.Status404NotFound);
            return;
        }

        if (endpoint.Metadata.GetMetadata<SpaceRoleMetadata>() is { } required && member > required.Minimum)
        {
            await Refuse(context, StatusCodes.Status403Forbidden);
            return;
        }

        tenant.SetSpace(spaceId);
        context.Features.Set(new SpaceAccess(spaceId, userId, member));
        await next(context);
    }

    #endregion

    #region Private Methods

    private static bool TryReadSpaceId(HttpContext context, out Guid spaceId)
    {
        spaceId = Guid.Empty;
        return context.GetRouteValue("spaceId") is string raw && Guid.TryParse(raw, out spaceId);
    }

    private static Task Refuse(HttpContext context, int statusCode) =>
        TypedResults.Problem(statusCode: statusCode).ExecuteAsync(context);

    #endregion
}
