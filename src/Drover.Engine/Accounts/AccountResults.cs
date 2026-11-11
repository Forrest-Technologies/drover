namespace Drover.Engine.Accounts;

public enum SetupOutcome
{
    Created,
    Closed,
    Invalid,
}

/// <summary>The outcome of creating the first account. <see cref="Problems"/> are validation messages, never secrets.</summary>
public sealed record SetupResult(SetupOutcome Status, Guid UserId, IReadOnlyList<string> Problems);

/// <summary>
/// The outcome of checking a password. A wrong password, an unknown email, and a locked account look the same to
/// the caller, so the answer does not say which emails exist.
/// </summary>
public sealed record CredentialResult(bool Succeeded, Guid UserId, string Email, string DisplayName)
{
    public static CredentialResult Refused { get; } = new(false, Guid.Empty, string.Empty, string.Empty);
}
