namespace Drover.Contracts.Auth;

/// <summary>
/// Input only: a person registering themselves. <c>AcceptedTermsVersion</c> is the version they were shown, and is
/// required only where the deployment requires acceptance. Never part of a response.
/// </summary>
public sealed record RegisterRequest(string Email, string DisplayName, string Password, string? AcceptedTermsVersion)
{
    public override string ToString() => nameof(RegisterRequest);
}
