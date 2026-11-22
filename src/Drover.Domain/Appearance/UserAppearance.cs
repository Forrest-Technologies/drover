namespace Drover.Domain.Appearance;

/// <summary>
/// The theme a person chose for every device that follows their account. One row per person; a device keeps its
/// own choice until the person asks it to use this one.
/// </summary>
public sealed class UserAppearance : IUserOwned
{
    #region Properties

    public Guid UserId { get; set; }

    public required string ThemeId { get; set; }

    /// <summary><c>system</c>, <c>dark</c> or <c>light</c>.</summary>
    public required string Mode { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    #endregion
}
