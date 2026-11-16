namespace Drover.Ui.Chat;

/// <summary>Whether this person can use a bot's screen, as far as the page knows.</summary>
public enum ScreenAccess
{
    /// <summary>Not asked yet, or the answer is on its way.</summary>
    Unknown,

    /// <summary>An owner or admin, and the bot's sandbox has a screen on a server with a provider.</summary>
    Usable,

    /// <summary>A member: owners and admins use a bot's screen.</summary>
    NotPermitted,

    /// <summary>The bot has no screen now: no provider, no sandbox, or Desktop off.</summary>
    Missing,

    /// <summary>The request is from a bot beneath, whose screen is its own and opens from its sandbox page.</summary>
    Elsewhere,
}
