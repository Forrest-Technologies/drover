namespace Drover.Data.Tests.Isolation;

/// <summary>The class a table belongs to, which decides the policy it needs. See docs/architecture/02-data-model.md.</summary>
public enum OwnershipClass
{
    /// <summary>Not <c>ISpaceOwned</c>, <c>IUserOwned</c>, or listed in <c>GlobalTables.manifest</c>.</summary>
    Unclassified,

    Space,

    SpaceWithAudience,

    User,

    Global,
}
