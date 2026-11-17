namespace Drover.Ui.Chat;

/// <summary>A bot's request for a person at its screen, as a card shows it: the call, the bot's reason, and where it stands.</summary>
public sealed record Takeover(Guid ToolCallId, string Reason, TakeoverPhase Phase)
{
    /// <summary>True while the bot waits for the person: asked, or the person holds the screen.</summary>
    public bool Waits => Phase is TakeoverPhase.Asked or TakeoverPhase.Holding;
}
