namespace Drover.Data.Identity;

/// <summary>A person who can sign in. Spaces are joined through <see cref="Membership"/>.</summary>
public sealed class User : IdentityUser<Guid>
{
    #region Constructors

    public User()
    {
        Id = Guid.CreateVersion7();
    }

    #endregion

    #region Properties

    public UserProfile Profile { get; set; } = new();

    public string DisplayName
    {
        get => Profile.DisplayName;
        set => Profile.DisplayName = value;
    }

    #endregion
}
