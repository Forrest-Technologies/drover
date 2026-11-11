namespace Drover.Domain.Models;

/// <summary>Where a model connection points. The key is never part of this record; callers pass it separately.</summary>
public sealed record ModelConnection(string Kind, Uri BaseAddress);
