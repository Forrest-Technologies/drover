namespace Drover.Server.Spaces;

/// <summary>Marks an endpoint that lives under a space: the route must carry <c>{spaceId}</c>.</summary>
public sealed record SpaceScopedMetadata;

/// <summary>The least role an endpoint accepts. Owner is the strongest role, so a lower value is a stronger role.</summary>
public sealed record SpaceRoleMetadata(MembershipRole Minimum);

/// <summary>Who is acting in which space, once the membership is confirmed.</summary>
public sealed record SpaceAccess(Guid SpaceId, Guid UserId, MembershipRole Role);

public static class SpaceAccessExtensions
{
    #region Public Methods

    public static SpaceAccess GetSpaceAccess(this HttpContext context) =>
        context.Features.Get<SpaceAccess>()
        ?? throw new InvalidOperationException("The membership middleware did not run for this endpoint.");

    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    /// <summary>The endpoint is open only to members of the space whose role is at least <paramref name="minimum"/>.</summary>
    public static TBuilder RequireSpaceRole<TBuilder>(this TBuilder builder, MembershipRole minimum)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SpaceRoleMetadata(minimum));

    #endregion
}
