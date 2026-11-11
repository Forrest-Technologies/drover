namespace Drover.Data.Identity;

/// <summary>A person who can sign in. Spaces are joined through <see cref="Membership"/>.</summary>
public sealed class User : IdentityUser<Guid>
{
    public User()
    {
        Id = Guid.CreateVersion7();
    }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The version of the terms of service this person accepted at sign-up, or null when none was asked.</summary>
    public string? TermsVersion { get; set; }

    public DateTimeOffset? TermsAcceptedAt { get; set; }
}
