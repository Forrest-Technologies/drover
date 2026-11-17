namespace Drover.Contracts.Auth;

/// <summary>
/// Input only: the one-time reset code the operator read from the server log, and the new password. Never part of a
/// response.
/// </summary>
public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword)
{
    public override string ToString() => nameof(ResetPasswordRequest);
}
