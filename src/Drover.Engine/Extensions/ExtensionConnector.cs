namespace Drover.Engine.Extensions;

/// <summary>What connecting an extension's tool server came to. With neither a state nor a problem, there is no such extension.</summary>
public sealed record ExtensionConnection(ExtensionState? State, string? Problem);

/// <summary>
/// Connects the tool server an installed extension names. The address is the file's, as it was imported: nothing in
/// the request can change it. The connection is made by <see cref="ToolConnectionService"/>, the path a connection
/// added by hand takes, so the same address rules, deployment rule, vault, and catalog apply, and what the server
/// returns is never trusted on a file's word. Opens its own tenant scopes and holds none while the server is asked
/// for its tools.
/// </summary>
public sealed class ExtensionConnector(
    ExtensionRepository extensions,
    ExtensionService service,
    ToolConnectionService connections,
    PolicyService policy,
    AuditLog audit,
    TenantScope scope,
    ILogger<ExtensionConnector> logger)
{
    #region Private Fields

    /// <summary>What connecting answers with when the address is local and the person has not said yes to that.</summary>
    public const string LocalNeedsYes = "This address is on this server's own network, or is not https. Tick the box to say you mean to connect to it.";

    #endregion

    #region Public Methods

    public async Task<ExtensionConnection> Connect(
        Guid installId,
        bool acceptLocalAddress,
        string? headerValue,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var (recipe, problem) = await FindRecipe(installId, cancellationToken);

        if (recipe is null)
        {
            return new(null, problem);
        }

        if (!policy.Deployment.AllowToolConnections)
        {
            return new(null, ToolConnectionService.TurnedOff);
        }

        if (ExtensionPackages.IsLocal(new Uri(recipe.Url.Trim())) && !acceptLocalAddress)
        {
            return new(null, LocalNeedsYes);
        }

        var made = await connections.Create(
            new NewToolConnection(recipe.Name, recipe.Url)
            {
                AuthKind = Words.Read<ToolAuthKind>(recipe.Auth) ?? ToolAuthKind.None,
                HeaderName = recipe.HeaderName,
                HeaderValue = headerValue,
            },
            cancellationToken);

        if (made.Connection is not { } connection)
        {
            return new(null, made.Problem);
        }

        if (!await Link(installId, connection, actorUserId, cancellationToken))
        {
            // Someone connected it in the meantime. Theirs stands, and this one is taken away again.
            await connections.Remove(connection.Id, cancellationToken);
            return new(null, AlreadyConnected(recipe));
        }

        // The connection is kept even when the server cannot be reached: its row then says why, as for any connection.
        await connections.Refresh(connection.Id, cancellationToken);
        return new(await Describe(installId, cancellationToken), null);
    }

    #endregion

    #region Private Methods

    private static string AlreadyConnected(ConnectionRecipe recipe) => $"{recipe.Name.Trim()} is already connected.";

    private async Task<(ConnectionRecipe? Recipe, string? Problem)> FindRecipe(Guid installId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await extensions.FindInstalled(installId, cancellationToken) is not { } installed)
        {
            return (null, null);
        }

        return ExtensionService.RecipeOf(installed.Install) switch
        {
            null => (null, "This extension names no tool server."),
            var recipe when installed.Install.ToolConnectionId is not null => (null, AlreadyConnected(recipe)),
            var recipe => (recipe, null),
        };
    }

    /// <summary>Writes the new connection on the extension's row. False when the row already has one.</summary>
    private async Task<bool> Link(Guid installId, ToolConnection connection, Guid actorUserId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await extensions.GetInstallForUpdate(installId, cancellationToken) is not { ToolConnectionId: null } install)
        {
            return false;
        }

        install.ToolConnectionId = connection.Id;
        audit.Record(
            actorUserId,
            "extension.connected",
            AuditTargets.Extension,
            install.Id,
            new JsonObject { ["tool_connection_id"] = null },
            new JsonObject { ["tool_connection_id"] = connection.Id });
        await work.Commit(cancellationToken);
        logger.LogInformation("Extension {InstallId} connected its tool server as {ConnectionId}", install.Id, connection.Id);
        return true;
    }

    private async Task<ExtensionState?> Describe(Guid installId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        return await service.Find(installId, cancellationToken);
    }

    #endregion
}
