namespace Drover.Domain.Bots;

/// <summary>Who shares a bot's sandbox. A bot with a sandbox defaults to <c>Private</c>.</summary>
public enum SandboxPolicy
{
    None,
    Private,
    Subtree,
    Named,
}
