namespace Drover.Engine.Accounts;

public enum PasswordOutcome
{
    Changed,
    Refused,
    Invalid,
}

/// <summary>
/// The outcome of changing or resetting a password. <see cref="Problems"/> are validation messages about the new
/// password, never secrets. <see cref="SecurityStamp"/> is the new stamp after a change, for the session that stays.
/// </summary>
public sealed record PasswordResult(PasswordOutcome Status, IReadOnlyList<string> Problems, string SecurityStamp = "")
{
    public static PasswordResult Refused { get; } = new(PasswordOutcome.Refused, []);
}
