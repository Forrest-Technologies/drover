namespace Drover.Contracts.Models;

/// <summary>Input only: a new model connection, with the key if the provider needs one. Never part of a response.</summary>
public sealed record CreateModelConnectionRequest(string Kind, string Name, string? BaseUrl, string? ApiKey, string? DefaultModel)
{
    public override string ToString() => nameof(CreateModelConnectionRequest);
}
