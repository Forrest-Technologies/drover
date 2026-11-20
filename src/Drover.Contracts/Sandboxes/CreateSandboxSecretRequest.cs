namespace Drover.Contracts.Sandboxes;

/// <summary>
/// Input only: a new sandbox secret with its value. Never part of a response, and prints nothing when logged.
/// <see cref="Mode"/> is <c>brokered</c> or <c>raw</c>; <see cref="Scope"/> is <c>space</c>, <c>bots</c>, or
/// <c>project</c>. Keeping the real value in the sandbox needs <see cref="ConfirmRawMode"/>.
/// </summary>
public sealed record CreateSandboxSecretRequest(
    string Name,
    string? Description,
    string Value,
    string Mode,
    IReadOnlyList<string>? Hosts,
    bool AllowInQuery,
    bool AllowInBody,
    string Scope,
    IReadOnlyList<Guid>? BotIds,
    Guid? ProjectId,
    bool ConfirmRawMode = false)
{
    public override string ToString() => nameof(CreateSandboxSecretRequest);
}
