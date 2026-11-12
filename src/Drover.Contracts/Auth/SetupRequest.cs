namespace Drover.Contracts.Auth;

/// <summary>Input only: the first account's details. Never part of a response.</summary>
public sealed record SetupRequest(string Email, string DisplayName, string Password, string? SetupCode)
{
    public override string ToString() => nameof(SetupRequest);
}
