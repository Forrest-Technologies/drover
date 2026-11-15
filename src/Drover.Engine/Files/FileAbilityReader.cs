namespace Drover.Engine.Files;

/// <summary>
/// What a bot can do with files right now: whether the catalog says its model reads pictures, and whether it has a
/// sandbox it may work in, which takes a sandbox provider on this server, a sandbox policy on the bot, and a sandbox
/// tool its effective grants do not deny. Runs inside the caller's tenant scope.
/// </summary>
public sealed class FileAbilityReader(
    ModelConnectionRepository connections,
    ToolRepository tools,
    SandboxAvailability sandboxes)
{
    #region Public Methods

    public async Task<FileAbilities> Read(Bot bot, CancellationToken cancellationToken)
    {
        var entry = await connections.FindCatalogEntry(bot.ModelConnectionId, bot.Model, cancellationToken);
        return new FileAbilities(entry?.Capabilities.Vision ?? false, await HasSandbox(bot, cancellationToken));
    }

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
