namespace Drover.Contracts.Auth;

/// <summary>Input only: a person's own credentials. Never part of a response.</summary>
public sealed record SignInRequest(string Email, string Password)
{
    public override string ToString() => nameof(SignInRequest);
}
