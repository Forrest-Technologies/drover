namespace Drover.Contracts.Bots;

/// <summary>
/// What a bot remembers about the person asking: whether memory is on for it, and every note it keeps about them,
/// newest first. <see cref="NearlyFull"/> is true once the notes come close to what they may hold, which is when the
/// person can act on it. The notes are listed with memory off too, so a person can always remove what is kept.
/// </summary>
public sealed record BotMemoryResponse(bool Enabled, bool NearlyFull, List<MemoryNoteResponse> Notes);

/// <summary>
/// One note. <see cref="WrittenBy"/> is "bot" or "person". <see cref="ChangedAt"/> is when it was written or last
/// changed. <see cref="Origin"/> is the message a bot wrote it from, when the person can still open that conversation.
/// </summary>
public sealed record MemoryNoteResponse(Guid Id, string Text, string WrittenBy, DateTimeOffset ChangedAt, MemoryNoteOrigin? Origin);

/// <summary>The message a note came from, and whether its conversation is the main one or a side chat.</summary>
public sealed record MemoryNoteOrigin(Guid ThreadId, Guid MessageId, bool IsMain);

/// <summary>The text of a note a person adds or rewrites.</summary>
public sealed record WriteNoteRequest(string? Text);

/// <summary>
/// A note just removed. <see cref="UndoToken"/> puts it back exactly as it was, for a short while: the server's own
/// sealed record of it, which a client can hand back but cannot read or change.
/// </summary>
public sealed record RemovedNoteResponse(Guid Id, string UndoToken);

public sealed record RestoreNoteRequest(string? UndoToken);
