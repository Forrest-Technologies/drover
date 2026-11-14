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

    /// <summary>The version of the terms of service this person accepted at sign-up, or null when none was asked.</summary>
    public string? TermsVersion { get; set; }

    public DateTimeOffset? TermsAcceptedAt { get; set; }

    /// <summary>
    /// Wrong passwords sent from a browser that has signed in to this account before. Counted apart from Identity's
    /// own counter, which counts everyone else, so strangers cannot lock the owner's own browser out.
    /// </summary>
    public int KnownBrowserAccessFailedCount { get; set; }

    /// <summary>Until when sign-in from a browser that has signed in before is locked, or null when it is not.</summary>
    public DateTimeOffset? KnownBrowserLockoutEnd { get; set; }

    #endregion
}
