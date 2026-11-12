namespace Drover.Data.Mappings;

public static class IdentityTableNames
{
    #region Public Methods

    /// <summary>Renames the ASP.NET Core Identity tables to Drover's snake_case names.</summary>
    public static ModelBuilder RenameIdentityTables(this ModelBuilder builder)
    {
        builder.Entity<User>().ToTable("users");
        builder.Entity<UserClaim>().ToTable("user_claims");
        builder.Entity<UserLogin>().ToTable("user_logins");
        builder.Entity<UserToken>().ToTable("user_tokens");
        return builder;
    }

    #endregion
}
