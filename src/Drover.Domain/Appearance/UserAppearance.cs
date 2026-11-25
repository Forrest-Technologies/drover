namespace Drover.Domain.Appearance;

/// <summary>The theme choice that follows one person's account across devices.</summary>
public sealed class UserAppearance : IUserOwned
{
    #region Properties

    public Guid UserId { get; set; }

    public required string ThemeId { get; set; }

    public required string Mode { get; set; }

    public required string Accent { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    #endregion
}
