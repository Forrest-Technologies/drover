namespace Drover.Data.Tests;

/// <summary>
/// Two spaces with their members, made through the app role the way the product will make them. Every test
/// builds its own, with new ids, so tests do not depend on each other's rows.
/// </summary>
public sealed record TenantWorld(Guid SpaceA, Guid SpaceB, Guid UserA1, Guid UserA2, Guid UserB1, Guid Traveler)
{
    #region Public Methods

    /// <summary>A1 and A2 are in space A, B1 is in space B, and the traveler is a member of both.</summary>
    public static async Task<TenantWorld> Seed(IServiceProvider provider)
    {
        var world = new TenantWorld(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        await Join(provider, world.SpaceA, world.UserA1, MembershipRole.Owner, createSpace: true, createUser: true);
        await Join(provider, world.SpaceA, world.UserA2, MembershipRole.Member, createSpace: false, createUser: true);
        await Join(provider, world.SpaceB, world.UserB1, MembershipRole.Owner, createSpace: true, createUser: true);
        await Join(provider, world.SpaceA, world.Traveler, MembershipRole.Member, createSpace: false, createUser: true);
        await Join(provider, world.SpaceB, world.Traveler, MembershipRole.Member, createSpace: false, createUser: false);
        return world;
    }

    #endregion

    #region Private Methods

    private static async Task Join(
        IServiceProvider provider,
        Guid spaceId,
        Guid userId,
        MembershipRole role,
        bool createSpace,
        bool createUser)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);

        if (createUser)
        {
            session.Db.Users.Add(NewUser(userId));
        }

        if (createSpace)
        {
            session.Db.Spaces.Add(new Space
            {
                Id = spaceId,
                Kind = SpaceKind.Shared,
                Name = $"Space {spaceId:N}",
                Slug = $"space-{spaceId:N}",
            });
        }

        session.Db.Memberships.Add(new Membership { UserId = userId, Role = role });
        await session.Commit();
    }

    private static User NewUser(Guid id) => new()
    {
        Id = id,
        UserName = $"user-{id:N}",
        NormalizedUserName = $"USER-{id:N}".ToUpperInvariant(),
        Email = $"{id:N}@example.test",
        NormalizedEmail = $"{id:N}@EXAMPLE.TEST".ToUpperInvariant(),
        DisplayName = $"User {id:N}",
    };

    #endregion
}
