namespace Drover.Engine.Files;

/// <summary>
/// What a bot can do with files in a conversation right now: whether the catalog says its model reads pictures and PDFs,
/// whether it has a sandbox it may work in, which takes a sandbox provider on this server, a sandbox policy on the
/// bot, and a sandbox tool its effective grants do not deny, and whether everyone who can reach that sandbox can see
/// the conversation. Runs inside the caller's tenant scope.
/// </summary>
public sealed class FileAbilityReader(
    ModelConnectionRepository connections,
    ToolRepository tools,
    AccountRepository accounts,
    SandboxAvailability sandboxes)
{
    #region Public Methods

    public async Task<FileAbilities> Read(Bot bot, ChatThread thread, CancellationToken cancellationToken)
    {
        var entry = await connections.FindCatalogEntry(bot.ModelConnectionId, bot.Model, cancellationToken);
        return new FileAbilities(
            entry?.EffectiveCapabilities.Vision ?? false,
            await HasSandbox(bot, cancellationToken),
            await AudienceCoversSandbox(thread.SpaceId, thread.AudienceUserId, accounts, cancellationToken),
            entry?.EffectiveCapabilities.PdfInput ?? false);
    }

    /// <summary>
    /// Whether everyone who can reach the space's sandboxes can see a conversation with this audience: it is shared
    /// with the whole space, or the space is personal and has exactly one member. Checked against the members as they
    /// are now, so a personal space that gains a member stops taking copies instead of leaking them.
    /// </summary>
    public static async Task<bool> AudienceCoversSandbox(Guid spaceId, Guid audienceUserId, AccountRepository accounts, CancellationToken cancellationToken) =>
        audienceUserId == Audience.Everyone || await accounts.IsPersonalWithOneMember(spaceId, cancellationToken);

    #endregion

    #region Private Methods

    private async Task<bool> HasSandbox(Bot bot, CancellationToken cancellationToken) =>
        sandboxes.IsConfigured
        && bot.SandboxPolicy is not SandboxPolicy.None
        && (await tools.ListGrantedTools(bot.Id, cancellationToken)).Any(granted =>
            SandboxTools.IsSandboxTool(granted.Tool.Name)
            && granted.Effect is not GrantEffect.Deny
            && granted.Tool.State is AvailabilityState.Available);

    #endregion
}
