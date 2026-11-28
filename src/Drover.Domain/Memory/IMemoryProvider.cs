namespace Drover.Domain.Memory;

/// <summary>
/// A memory service that learns about a bot's person from their private conversations and recalls what it knows for
/// later runs. Every identifier sent to a provider is a Drover GUID, never a name, email, or bot name. Failures are
/// reported as <see cref="MemoryProviderException"/>.
/// </summary>
public interface IMemoryProvider
{
    string Kind { get; }

    Task Remember(MemoryScope scope, IReadOnlyList<MemoryTurn> turns, CancellationToken cancellationToken);

    Task<MemoryRecall?> Recall(MemoryScope scope, MemoryRecallRequest request, CancellationToken cancellationToken);

    Task ForgetConversation(MemoryScope scope, CancellationToken cancellationToken);

    Task ForgetPerson(MemoryPersonScope scope, CancellationToken cancellationToken);
}


/// <summary>Identifies one private conversation between one bot and one person in one space, by Drover GUIDs only.</summary>
public sealed record MemoryScope(Guid SpaceId, Guid BotId, Guid PersonId, Guid ThreadId);


/// <summary>Identifies one person in one space, by Drover GUIDs only.</summary>
public sealed record MemoryPersonScope(Guid SpaceId, Guid PersonId);


/// <summary>One spoken turn from a private conversation.</summary>
public sealed record MemoryTurn(MemorySpeaker Speaker, string Text, DateTimeOffset At);


/// <summary>Who spoke a turn sent to the memory service.</summary>
public enum MemorySpeaker
{
    Person,
    Bot,
}


/// <summary>What Drover asks a memory service for before a run's first model call.</summary>
public sealed record MemoryRecallRequest(string? LatestMessage, int MaxCharacters);


/// <summary>What a memory service knows about the person for a bot: discrete facts and an optional summary.</summary>
public sealed record MemoryRecall(IReadOnlyList<string> Facts, string? Summary);
