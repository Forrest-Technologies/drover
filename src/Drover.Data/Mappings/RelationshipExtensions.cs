namespace Drover.Data.Mappings;

public static class RelationshipExtensions
{
    #region Public Methods

    /// <summary>
    /// A foreign key to another space-owned table, always composite: <c>(space_id, x_id)</c> references
    /// <c>(space_id, id)</c>, so a row cannot point at another space's row. With <paramref name="audience"/> the
    /// key also carries <c>audience_user_id</c>, so the child cannot reach more people than its parent, and an
    /// audience change on the parent is carried to the child (the migration adds <c>ON UPDATE CASCADE</c>).
    /// </summary>
    public static ReferenceCollectionBuilder<TParent, TChild> WithinSpace<TChild, TParent>(
        this EntityTypeBuilder<TChild> builder,
        string foreignKey,
        bool audience = false)
        where TChild : class
        where TParent : class
    {
        string[] dependent = audience
            ? [nameof(ISpaceOwned.SpaceId), foreignKey, nameof(IAudienceScoped.AudienceUserId)]
            : [nameof(ISpaceOwned.SpaceId), foreignKey];
        string[] principal = audience
            ? [nameof(ISpaceOwned.SpaceId), "Id", nameof(IAudienceScoped.AudienceUserId)]
            : [nameof(ISpaceOwned.SpaceId), "Id"];

        return builder.HasOne<TParent>()
            .WithMany()
            .HasForeignKey(dependent)
            .HasPrincipalKey(principal)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>A foreign key to <c>users</c>. A user row is global, so the key needs no space.</summary>
    public static ReferenceCollectionBuilder<User, TChild> ReferencesUser<TChild>(
        this EntityTypeBuilder<TChild> builder,
        string foreignKey)
        where TChild : class =>
        builder.HasOne<User>().WithMany().HasForeignKey(foreignKey).OnDelete(DeleteBehavior.Restrict);

    #endregion
}
