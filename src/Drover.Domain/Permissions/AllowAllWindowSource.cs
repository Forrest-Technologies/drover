namespace Drover.Domain.Permissions;

/// <summary>Where a window was started. The names are the strings the database stores.</summary>
public enum AllowAllWindowSource
{
    /// <summary>The quick control on a bot.</summary>
    Bot,

    /// <summary>An approval card, for the call that offered Allow all.</summary>
    Approval,
}
