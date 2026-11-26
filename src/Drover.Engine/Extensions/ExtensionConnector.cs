namespace Drover.Engine.Extensions;

/// <summary>What connecting an extension's tool server came to. With neither a state nor a problem, there is no such extension.</summary>
public sealed record ExtensionConnection(ExtensionState? State, string? Problem);

/// <summary>
/// The steps of an installed extension that reach its tool connection: connecting the server the file names, and
/// removing the extension with the connection it made. The address is the file's, with the extension's settings
/// filled in: nothing else in the request can change it. The connection is made by
/// <see cref="ToolConnectionService"/>, the path a connection added by hand takes, so the same address rules,
/// deployment rule, vault, and catalog apply, and what the server returns is never trusted on a file's word. Opens
/// its own tenant scopes and holds none while the server is asked for its tools.
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

    public async Task<ExtensionConnection> Connect(Guid installId, ConnectExtensionRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var (package, recipe, problem) = await FindRecipe(installId, cancellationToken);

        if (package is null || recipe is null)
        {
            return new(null, problem);
        }

        if (!policy.Deployment.AllowToolConnections)
        {
            return new(null, ToolConnectionService.TurnedOff);
        }

        var (values, valueProblem) = ReadSettings(package, request.Settings);

        if (valueProblem is not null)
        {
            return new(null, valueProblem);
        }

        var kept = package.Settings
            .Where(setting => setting.Type is not ExtensionSetting.Secret)
            .ToDictionary(setting => setting.Key, setting => values[setting.Key]);
        var url = ExtensionPackages.Fill(recipe.Url.Trim(), kept);

        if (ToolConnectionService.FindAddressProblem(recipe.Name, url) is { } addressProblem)
        {
            return new(null, $"With these settings the address is not one Drover can connect to. {addressProblem}");
        }

        if (ExtensionPackages.IsLocal(new Uri(url)) && !request.AcceptLocalAddress)
        {
            return new(null, LocalNeedsYes);
        }

        var made = await connections.Create(
            new NewToolConnection(recipe.Name, url)
            {
                AuthKind = Words.Read<ToolAuthKind>(recipe.Auth) ?? ToolAuthKind.None,
                HeaderName = recipe.HeaderName,
                HeaderValue = recipe.HeaderTemplate is { } written ? ExtensionPackages.Fill(written, values) : request.HeaderValue,
            },
            cancellationToken);

        if (made.Connection is not { } connection)
        {
            return new(null, made.Problem);
        }

        if (!await Link(installId, connection, kept, actorUserId, cancellationToken))
        {
            // Someone connected it in the meantime. Theirs stands, and this one is taken away again.
            await connections.Remove(connection.Id, cancellationToken);
            return new(null, AlreadyConnected(recipe));
        }

        // The connection is kept even when the server cannot be reached: its row then says why, as for any connection.
        await connections.Refresh(connection.Id, cancellationToken);
        return new(await Describe(installId, cancellationToken), null);
    }

    /// <summary>
    /// Removes the extension: its row, the settings kept on it, and the tool connection it made, with that
    /// connection's tools, grants, and credentials. Its themes are no longer offered and its rules no longer
    /// suggested. A rule a person already added to a bot is the bot's own and stays. False when there is no such
    /// extension.
    /// </summary>
    public async Task<bool> Remove(Guid installId, Guid actorUserId, CancellationToken cancellationToken)
    {
        Guid? connectionId;

        await using (var work = await scope.Begin(cancellationToken))
        {
            if (await extensions.FindInstalled(installId, cancellationToken) is not { } found)
            {
                return false;
            }

            connectionId = found.ToolConnectionId;
        }

        if (connectionId is { } made)
        {
            await connections.Remove(made, cancellationToken);
        }

        await using (var work = await scope.Begin(cancellationToken))
        {
            if (await extensions.GetInstallForUpdate(installId, cancellationToken) is not { } install)
            {
                return false;
            }

            extensions.Remove(install);
            audit.Record(actorUserId, "extension.removed", AuditTargets.Extension, install.Id, ExtensionService.Snapshot(install), null);
            await work.Commit(cancellationToken);
            logger.LogInformation("Extension {InstallId} removed", install.Id);
        }

        return true;
    }

    #endregion

    #region Private Methods

    private static string AlreadyConnected(ConnectionRecipe recipe) => $"{recipe.Name.Trim()} is already connected.";

    /// <summary>A value for every setting the file declares: what the person gave, or what the file starts it with.</summary>
    private static (Dictionary<string, string> Values, string? Problem) ReadSettings(ExtensionPackage package, IReadOnlyDictionary<string, string>? given)
    {
        Dictionary<string, string> values = [];

        foreach (var setting in package.Settings)
        {
            var value = given is not null && given.TryGetValue(setting.Key, out var typed) ? typed : setting.Default;

            if (ExtensionPackages.FindValueProblem(setting, value) is { } problem)
            {
                return (values, problem);
            }

            values[setting.Key] = setting.Type is ExtensionSetting.Secret ? value! : value!.Trim();
        }

        return (values, null);
    }

    private async Task<(ExtensionPackage? Package, ConnectionRecipe? Recipe, string? Problem)> FindRecipe(Guid installId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await extensions.FindInstalled(installId, cancellationToken) is not { } install)
        {
            return (null, null, null);
        }

        var package = ExtensionPackages.ReadKept(install.Package);

        return ExtensionService.RecipeOf(package) switch
        {
            null => (null, null, "This extension names no tool server."),
            var recipe when install.ToolConnectionId is not null => (null, null, AlreadyConnected(recipe)),
            var recipe => (package, recipe, null),
        };
    }

    /// <summary>Writes the new connection and the settings it was made with on the extension's row. False when the row already has a connection.</summary>
    private async Task<bool> Link(Guid installId, ToolConnection connection, Dictionary<string, string> kept, Guid actorUserId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await extensions.GetInstallForUpdate(installId, cancellationToken) is not { ToolConnectionId: null } install)
        {
            return false;
        }

        install.ToolConnectionId = connection.Id;
        install.Settings = kept;
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
