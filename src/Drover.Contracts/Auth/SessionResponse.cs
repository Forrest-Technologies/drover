namespace Drover.Contracts.Auth;

/// <summary>Who is signed in and which spaces they can pick from.</summary>
public sealed record SessionResponse(Guid UserId, string Email, string DisplayName, IReadOnlyList<SpaceSummary> Spaces);
