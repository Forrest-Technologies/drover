namespace Drover.Data.Identity;

/// <summary>The only user fields shared with fellow members of the selected space.</summary>
public sealed class UserProfile
{
    #region Properties

    public Guid UserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    #endregion
}
