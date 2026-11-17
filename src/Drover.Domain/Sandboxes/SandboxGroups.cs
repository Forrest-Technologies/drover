namespace Drover.Domain.Sandboxes;

/// <summary>A bot on the path from one bot to the top of its tree: itself at distance 0, its parent at 1, and so on.</summary>
public sealed record SandboxPathEntry(Guid BotId, int Distance, SandboxPolicy Policy);


/// <summary>
/// Which sandbox a bot works in, and who else may. A sharing group is named by the bot that owns its sandbox, so the
/// group id is that bot's id. Pure: the caller loads the path and the shares.
/// </summary>
public static class SandboxGroups
{
    #region Public Methods

    /// <summary>
    /// The owner of the group the bot is in. In a <see cref="SandboxPolicy.Subtree"/> share the highest bot on the path
    /// whose policy is subtree owns it, whatever the bots below it say for themselves; otherwise the bot owns its own.
    /// A bot in a <see cref="SandboxPolicy.Named"/> share is still the owner of its own sandbox: it reaches the shared
    /// one only by naming it.
    /// </summary>
    public static Guid OwnerOf(Guid botId, IReadOnlyList<SandboxPathEntry> path) =>
        path
            .Where(entry => entry.Policy is SandboxPolicy.Subtree)
            .OrderByDescending(entry => entry.Distance)
            .Select(entry => entry.BotId)
            .DefaultIfEmpty(botId)
            .First();

    /// <summary>
    /// Whether a bot may work in the sandbox <paramref name="ownerBotId"/> owns: it is in that group, or the owner shares
    /// by name and lists it. Siblings and parents get nothing else.
    /// </summary>
    public static bool MayEnter(
        Guid callerBotId,
        Guid callerOwnerBotId,
        Guid ownerBotId,
        SandboxPolicy ownerPolicy,
        IReadOnlyCollection<Guid> ownerShares) =>
        ownerPolicy is not SandboxPolicy.None
        && (callerOwnerBotId == ownerBotId || (ownerPolicy is SandboxPolicy.Named && ownerShares.Contains(callerBotId)));

    /// <summary>The outbound network a bot's sandbox settings ask for.</summary>
    public static NetworkPolicy ToNetworkPolicy(this SandboxSettings settings) =>
        new(settings.Network, settings.Network is NetworkAccess.Allowlist ? settings.AllowedHosts : []);

    #endregion
}
