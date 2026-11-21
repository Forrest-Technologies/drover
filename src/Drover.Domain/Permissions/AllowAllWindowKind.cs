namespace Drover.Domain.Permissions;

/// <summary>How long a window runs. The database stores the spellings in <c>AllowAllWindowKindConverter</c>.</summary>
public enum AllowAllWindowKind
{
    /// <summary>Thirty minutes from <c>started_at</c>.</summary>
    Minutes30,

    /// <summary>One hour from <c>started_at</c>.</summary>
    Hour1,

    /// <summary>Until the bot has nothing queued, running, or waiting for this person.</summary>
    UntilDone,

    /// <summary>Until the person turns it off with Ask again.</summary>
    UntilOff,
}
