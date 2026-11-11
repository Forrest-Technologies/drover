namespace Drover.Data.Identity;

/// <summary>A token stored for one user. Marked user-owned so the isolation suite covers the table.</summary>
public sealed class UserToken : IdentityUserToken<Guid>, IUserOwned
{
}
