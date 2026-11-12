namespace Drover.Domain.Permissions;

/// <summary>One bot's grant for one tool, as stored.</summary>
public readonly record struct GrantEntry(GrantEffect Effect, ReviewState ReviewState = ReviewState.Current);
