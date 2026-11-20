namespace Drover.Contracts.Sandboxes;

/// <summary>
/// Which bots a sandbox secret is for: <c>space</c> (every bot), <c>bots</c> with the ids, or <c>project</c> with its id.
/// Giving a value kept in the sandbox to more bots needs <see cref="ConfirmRawMode"/>, as keeping it there did.
/// </summary>
public sealed record ChangeSandboxSecretScopeRequest(string Scope, IReadOnlyList<Guid>? BotIds, Guid? ProjectId, bool ConfirmRawMode = false);

/// <summary>
/// Where a sandbox secret's value may go. Changing a secret to <c>raw</c> needs <see cref="ConfirmRawMode"/>, so
/// no client can put a value into a sandbox without a person saying so.
/// </summary>
public sealed record ChangeSandboxSecretBindingRequest(
    string Mode,
    IReadOnlyList<string>? Hosts,
    bool AllowInQuery,
    bool AllowInBody,
    bool ConfirmRawMode = false);

public sealed record ChangeSandboxSecretDescriptionRequest(string? Description);

/// <summary>One thing done to a sandbox secret, newest first. Never holds the value.</summary>
public sealed record SandboxSecretHistoryEntry(
    string Action,
    string ActorKind,
    Guid ActorId,
    string? ActorName,
    DateTimeOffset At);

/// <summary>
/// A name a bot's sandbox is given, without its value, and where it comes from: <c>space</c>, <c>bot</c>, or
/// <c>project</c>. Brokered names list the hosts they work for.
/// </summary>
public sealed record BotSandboxSecretName(
    string Name,
    string Description,
    string Mode,
    IReadOnlyList<string> Hosts,
    string From,
    string? ProjectName);
