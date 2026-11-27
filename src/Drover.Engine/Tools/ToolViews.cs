namespace Drover.Engine.Tools;

/// <summary>Maps tool connections and their tools to what the API carries. Nothing here can reach a credential.</summary>
public static class ToolViews
{
    #region Public Methods

    /// <summary>
    /// <paramref name="signIn"/> is how the caller's own sign-in stands (or the space's, where one serves everyone),
    /// from <see cref="ToolConnectionService.ListSignIns"/>; null means there is none.
    /// </summary>
    public static ToolConnectionSummary ToSummary(this ToolConnection connection, CredentialStatus? signIn = null) =>
        new(
            connection.Id,
            connection.Name,
            connection.Slug,
            connection.Url,
            WireName(connection.AuthKind),
            WireName(connection.Trust),
            WireName(connection.ProtocolMode),
            WireName(connection.Status),
            connection.NegotiatedProtocol,
            connection.LastListedAt,
            connection.LastError)
        {
            SignInScope = WireName(connection.CredentialScope),
            Issuer = connection.Oauth?.Issuer,
            SignIn = connection.AuthKind is not ToolAuthKind.Oauth
                ? null
                : signIn switch
                {
                    CredentialStatus.Active => "signed_in",
                    null => "needs_sign_in",
                    _ => "needs_reconnect",
                },
        };

    /// <summary>
    /// One summary per tool, with the name the model will actually see. Names are worked out over the whole catalog
    /// of the connection, so two tools whose names clash are reported under two different names.
    /// </summary>
    public static List<ConnectionToolSummary> Summaries(this IEnumerable<CatalogTool> tools, ToolConnection connection)
    {
        var listed = tools.ToList();
        var names = ToolNames.ForConnection(
            connection.Slug,
            [.. listed.Select(tool => new ToolName(
                tool.Tool.Id,
                connection.Id,
                tool.Tool.Name,
                tool.Tool.FirstSeenAt))]);
        return [.. listed.Select(tool => tool.ToSummary(connection, names[tool.Tool.Id]))];
    }

    public static ConnectionToolSummary ToSummary(this CatalogTool listed, ToolConnection connection, string modelName)
    {
        var tool = listed.Tool;
        var reviewed = listed.NeedsReview && tool.ReviewedDefinition is { } kept ? kept.Deserialize<ReviewedToolDefinition>() : null;
        return new ConnectionToolSummary(
            tool.Id,
            connection.Id,
            tool.Name,
            modelName,
            tool.Title,
            tool.Description,
            WireName(tool.SensitivityOn(connection)),
            WireName(tool.State),
            tool.LastSeenAt)
        {
            InputSchema = tool.InputSchema.GetRawText(),
            NeedsReview = listed.NeedsReview,
            DefinitionHash = tool.DefinitionHash,
            Reviewed = reviewed is null ? null : new ToolDefinitionSnapshot(reviewed.Title, reviewed.Description, reviewed.InputSchema),
        };
    }

    public static ToolConnectionRefresh ToResponse(this ToolRefresh refresh, CredentialStatus? signIn = null) =>
        new(
            refresh.Connection.ToSummary(signIn),
            [.. refresh.Tools.Summaries(refresh.Connection)],
            refresh.Failure is null ? null : refresh.Message,
            refresh.Change?.Added ?? 0,
            refresh.Change?.Changed ?? 0,
            refresh.Change?.Missing ?? 0,
            refresh.Change?.GrantsForReview ?? 0);

    #endregion

    #region Private Methods

    /// <summary>An enum value as the API writes it: snake case, as the database stores it.</summary>
    private static string WireName<T>(T value)
        where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) =>
            char.IsUpper(character) && index > 0 ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    #endregion
}
