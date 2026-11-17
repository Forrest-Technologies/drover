namespace Drover.Contracts.Auth;

/// <summary>Input only: a signed-in person's current password and the one replacing it. Never part of a response.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword)
{
    public override string ToString() => nameof(ChangePasswordRequest);
}
