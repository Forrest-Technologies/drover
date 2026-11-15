namespace Drover.Contracts.Threads;

public sealed record ConversationSummary(string? Text, int MessagesCovered, DateTimeOffset? UpdatedAt);
