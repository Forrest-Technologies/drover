namespace Drover.Domain.Permissions;

/// <summary>Why an allow-all window stopped being open. The names are the strings the database stores.</summary>
public enum AllowAllWindowEndReason
{
    /// <summary>The server clock passed <c>ends_at</c>.</summary>
    Expired,

    /// <summary>The bot has nothing queued, running, or waiting for the window's person.</summary>
    WorkDone,

    /// <summary>The person chose Ask again, or turned on a new window in place of this one.</summary>
    AskedAgain,

    /// <summary>The bot was paused.</summary>
    BotPaused,

    /// <summary>The bot was removed.</summary>
    BotRemoved,

    /// <summary>The window's person left the space.</summary>
    MemberLeft,
}
