namespace Drover.Contracts.Auth;

/// <summary>One space as the signed-in member sees it, with their role.</summary>
public sealed record SpaceResponse(Guid Id, string Kind, string Name, string Slug, string Role);
