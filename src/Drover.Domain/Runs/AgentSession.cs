namespace Drover.Domain.Runs;

/// <summary>
/// The agent framework's serialized session for one bot in one thread. Opaque working state: if a framework update
/// cannot read it, it is discarded and rebuilt from the messages. Nothing may depend on reading it.
/// </summary>
public sealed class AgentSession : ISpaceOwned, IAudienceScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public Guid ThreadId { get; set; }

    public Guid BotId { get; set; }

    public JsonElement State { get; set; }

    public required string FrameworkVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
