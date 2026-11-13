namespace Drover.Server.Tools;

/// <summary>
/// Tool connections of a space. Any member may see which connections and tools exist; only an admin adds, changes,
/// refreshes, or removes one. The service opens its own short tenant scopes, because refreshing waits on the tool
/// server and no transaction may be open across that, so these endpoints sit outside the tenant scope filter. No
/// response carries a credential.
/// </summary>
public static class ToolConnectionEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapToolConnectionEndpoints(this RouteGroupBuilder space)
    {
        var connections = space.MapGroup("/tool-connections");
        connections.MapGet("/", ListConnections).WithName("ListToolConnections");
        connections.MapGet("/{connectionId:guid}/tools", ListTools).WithName("ListConnectionTools");
        connections.MapPost("/", CreateConnection).RequireSpaceRole(MembershipRole.Admin).WithName("CreateToolConnection")
            .WithSummary("Adds a tool connection and lists its tools. The header value, if any, goes to the vault and is never returned.");
        connections.MapPut("/{connectionId:guid}", UpdateConnection).RequireSpaceRole(MembershipRole.Admin).WithName("UpdateToolConnection");
        connections.MapPut("/{connectionId:guid}/header", ReplaceHeader).RequireSpaceRole(MembershipRole.Admin).WithName("ReplaceToolHeader");
        connections.MapPost("/{connectionId:guid}/refresh", Refresh).RequireSpaceRole(MembershipRole.Admin).WithName("RefreshToolConnection")
            .WithSummary("Asks the server for its tools now. A changed tool definition marks its grants for review.");
        connections.MapPost("/{connectionId:guid}/tools/{toolId:guid}/review", ConfirmReview).RequireSpaceRole(MembershipRole.Admin).WithName("ConfirmToolReview")
            .WithSummary("Accepts a tool's changed definition after reading it. Until then the tool is offered to no bot.");
        connections.MapDelete("/{connectionId:guid}", RemoveConnection).RequireSpaceRole(MembershipRole.Admin).WithName("RemoveToolConnection");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Ok<List<ToolConnectionSummary>>> ListConnections(HttpContext context, ToolConnectionService connections) =>
        TypedResults.Ok((await connections.ListConnections(context.RequestAborted)).Select(connection => connection.ToSummary()).ToList());

    private static async Task<Results<Ok<List<ConnectionToolSummary>>, NotFound>> ListTools(
        Guid connectionId,
        HttpContext context,
        ToolConnectionService connections)
    {
        var connection = await connections.FindConnection(connectionId, context.RequestAborted);
        var tools = connection is null ? null : await connections.ListTools(connectionId, context.RequestAborted);
        return tools is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(tools.Select(tool => tool.ToSummary(connection!)).ToList());
    }

    private static async Task<Results<Created<ToolConnectionRefresh>, ProblemHttpResult>> CreateConnection(
        CreateToolConnectionRequest request,
        HttpContext context,
        ToolConnectionService connections)
    {
        if (!TryRead<ToolAuthKind>(request.AuthKind, ToolAuthKind.None, out var authKind)
            || !TryRead<TrustLevel>(request.Trust, TrustLevel.Untrusted, out var trust)
            || !TryRead<ProtocolMode>(request.ProtocolMode, ProtocolMode.Automatic, out var mode))
        {
            return TypedResults.Problem(title: "One of the connection's settings has a value this server does not know.", statusCode: StatusCodes.Status400BadRequest);
        }

        var change = await connections.Create(
            new NewToolConnection(request.Name, request.Url)
            {
                AuthKind = authKind,
                HeaderName = request.HeaderName,
                HeaderValue = request.HeaderValue,
                Trust = trust,
                ProtocolMode = mode,
            },
            context.RequestAborted);

        if (change.Connection is not { } connection)
        {
            return TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
        }

        // The connection is saved whatever the server answers; the answer says whether its tools could be listed.
        var listed = await connections.Refresh(connection.Id, context.RequestAborted);
        return TypedResults.Created(
            $"/api/spaces/{connection.SpaceId}/tool-connections/{connection.Id}/tools",
            listed?.ToResponse() ?? new ToolConnectionRefresh(connection.ToSummary(), [], null, 0, 0, 0, 0));
    }

    private static async Task<Results<Ok<ToolConnectionSummary>, NotFound, ProblemHttpResult>> UpdateConnection(
        Guid connectionId,
        UpdateToolConnectionRequest request,
        HttpContext context,
        ToolConnectionService connections)
    {
        if (!TryRead<TrustLevel>(request.Trust, TrustLevel.Untrusted, out var trust) || !TryRead<ProtocolMode>(request.ProtocolMode, ProtocolMode.Automatic, out var mode))
        {
            return TypedResults.Problem(title: "One of the connection's settings has a value this server does not know.", statusCode: StatusCodes.Status400BadRequest);
        }

        var change = await connections.Update(connectionId, trust, mode, context.RequestAborted);
        return change.Connection is { } connection ? TypedResults.Ok(connection.ToSummary()) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ToolConnectionSummary>, NotFound, ProblemHttpResult>> ReplaceHeader(
        Guid connectionId,
        ReplaceToolHeaderRequest request,
        HttpContext context,
        ToolConnectionService connections)
    {
        var change = await connections.ReplaceHeader(connectionId, request.HeaderName, request.HeaderValue, context.RequestAborted);

        if (change.Connection is { } connection)
        {
            return TypedResults.Ok(connection.ToSummary());
        }

        return change.Problem is null
            ? TypedResults.NotFound()
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ToolConnectionRefresh>, NotFound>> Refresh(
        Guid connectionId,
        HttpContext context,
        ToolConnectionService connections) =>
        await connections.Refresh(connectionId, context.RequestAborted) is { } listed
            ? TypedResults.Ok(listed.ToResponse())
            : TypedResults.NotFound();

    private static async Task<Results<Ok<ConnectionToolSummary>, NotFound>> ConfirmReview(
        Guid connectionId,
        Guid toolId,
        HttpContext context,
        ToolConnectionService connections)
    {
        var connection = await connections.FindConnection(connectionId, context.RequestAborted);
        var tool = connection is null ? null : await connections.ConfirmReview(connectionId, toolId, context.RequestAborted);
        return tool is null ? TypedResults.NotFound() : TypedResults.Ok(tool.ToSummary(connection!));
    }

    private static async Task<Results<NoContent, NotFound>> RemoveConnection(Guid connectionId, HttpContext context, ToolConnectionService connections) =>
        await connections.Remove(connectionId, context.RequestAborted) ? TypedResults.NoContent() : TypedResults.NotFound();

    #endregion

    #region Private Methods

    /// <summary>Reads a setting by its wire name (snake case). A missing value takes the default; an unknown one is refused.</summary>
    private static bool TryRead<T>(string? text, T fallback, out T value)
        where T : struct, Enum
    {
        value = fallback;
        return string.IsNullOrWhiteSpace(text)
            || Enum.TryParse(text.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    #endregion
}
