namespace Drover.Data.Identity;

/// <summary>An external sign-in linked to one user. Marked user-owned so the isolation suite covers the table.</summary>
public sealed class UserLogin : IdentityUserLogin<Guid>, IUserOwned
{
}
