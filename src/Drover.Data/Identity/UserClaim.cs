namespace Drover.Data.Identity;

/// <summary>A claim held by one user. Marked user-owned so the isolation suite covers the table.</summary>
public sealed class UserClaim : IdentityUserClaim<Guid>, IUserOwned
{
}
