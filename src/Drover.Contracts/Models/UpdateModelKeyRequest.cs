namespace Drover.Contracts.Models;

/// <summary>Input only: the new key for a connection. Never part of a response.</summary>
public sealed record UpdateModelKeyRequest(string ApiKey)
{
    public override string ToString() => nameof(UpdateModelKeyRequest);
}
