namespace Drover.Domain.Models;

/// <summary>Transport destination only. Persistent connection rows and credentials remain separate.</summary>
public sealed record ModelEndpoint(string Kind, Uri BaseAddress);
