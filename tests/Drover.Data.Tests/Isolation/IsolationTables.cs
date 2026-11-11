namespace Drover.Data.Tests.Isolation;

/// <summary>
/// The tables each generated check runs on, found from the entity model, so a new table joins the right checks
/// without anyone listing it. The scratch pair is always included: it keeps checks that no real table needs yet
/// from going unrun, and shows they work.
/// </summary>
public static class IsolationTables
{
    #region Private Fields

    private static readonly OwnershipClass[] spaceClasses = [OwnershipClass.Space, OwnershipClass.SpaceWithAudience];

    private static readonly string[] scratchAudienceTables = ["scratch_parent", "scratch_child"];

    #endregion

    #region Properties

    /// <summary>Every scratch table starts with this, so a harness can tell it from a real table.</summary>
    public static string ScratchPrefix => "scratch_";

    #endregion

    #region Public Methods

    /// <summary>Tables with <c>space_id</c>.</summary>
    public static TheoryData<string> Space() =>
        [.. OwnershipMap.Current.InClass(spaceClasses).Select(table => table.Table), .. scratchAudienceTables];

    /// <summary>Tables with <c>space_id</c> that point at another space-owned table.</summary>
    public static TheoryData<string> SpaceWithSpaceOwnedParent() =>
    [
        .. spaceClasses
            .SelectMany(tableClass => OwnershipMap.Current.InClassWithParentIn(tableClass, spaceClasses))
            .Select(table => table.Table),
        "scratch_child",
    ];

    /// <summary>Tables with <c>audience_user_id</c>.</summary>
    public static TheoryData<string> Audience() =>
        [.. OwnershipMap.Current.InClass(OwnershipClass.SpaceWithAudience).Select(table => table.Table), .. scratchAudienceTables];

    /// <summary>Tables with <c>audience_user_id</c> that point at a table with one.</summary>
    public static TheoryData<string> AudienceWithAudienceParent() =>
    [
        .. OwnershipMap.Current
            .InClassWithParentIn(OwnershipClass.SpaceWithAudience, OwnershipClass.SpaceWithAudience)
            .Select(table => table.Table),
        "scratch_child",
    ];

    /// <summary>Tables with <c>user_id</c>.</summary>
    public static TheoryData<string> User() =>
        [.. OwnershipMap.Current.InClass(OwnershipClass.User).Select(table => table.Table)];

    #endregion
}
