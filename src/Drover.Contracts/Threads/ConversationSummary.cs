namespace Drover.Contracts.Threads;

/// <summary>
/// The server's running summary of a person's main conversation with a bot. <see cref="Text"/> is null until the
/// first summary is written, and always for a side chat. <see cref="MessagesCovered"/> is how many of the
/// conversation's messages it covers, from the first: every message whose position is at most that number.
/// <see cref="UpdatedAt"/> is when it was last rewritten. <see cref="ServerWrites"/> is false on a server whose
/// deployment policy has summaries turned off: one written before stays readable, and no new one will come.
/// </summary>
public sealed record ConversationSummary(string? Text, int MessagesCovered, DateTimeOffset? UpdatedAt, bool ServerWrites = true);
