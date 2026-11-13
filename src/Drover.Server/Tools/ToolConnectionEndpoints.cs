namespace Drover.Server.Tools;

/// <summary>
/// Tool connections of a space. Any member may see which connections and tools exist; only an admin adds, changes,
/// refreshes, or removes one. The service opens its own short tenant scopes, because refreshing waits on the tool
/// server and no transaction may be open across that, so these endpoints sit outside the tenant scope filter. No
/// response carries a credential. Signing in to a connection is each member's own act; where one sign-in serves the
/// whole space, it is an admin's.
/// </summary>
public static class ToolConnectionEndpoints
{
    #region Private Fields

    private static readonly string[] clientGrantTypes = ["authorization_code", "refresh_token"];

    private static readonly string[] clientResponseTypes = ["code"];

    #endregion

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
        connections.MapPost("/{connectionId:guid}/sign-in", BeginSignIn).WithName("BeginToolSignIn")
            .WithSummary("Starts a sign-in to the tool server and says where the person's browser must go. Tokens go to the vault and are never returned.");
        connections.MapDelete("/{connectionId:guid}/sign-in", SignOut).WithName("RemoveToolSignIn")
            .WithSummary("Forgets the caller's sign-in to the tool server, or the space's where one sign-in serves everyone.");
        connections.MapDelete("/{connectionId:guid}", RemoveConnection).RequireSpaceRole(MembershipRole.Admin).WithName("RemoveToolConnection");
        return space;
    }

    #endregion

    #region Endpoints

    /// <summary>
    /// The provider's sign-in page sends the browser here, outside any space: the <c>state</c> says which sign-in
    /// it is, and only the signed-in person who started it can complete it. The answer is a redirect into the app
    /// that says how it went and carries nothing else.
    /// </summary>
    public static IEndpointRouteBuilder MapToolSignInCallback(this IEndpointRouteBuilder app)
    {
        app.MapGet(ToolSignInOptions.CallbackPath, CompleteSignIn).RequireAuthorization().ExcludeFromDescription();
        app.MapGet(ToolSignInOptions.ClientDocumentPath, DescribeClient).AllowAnonymous().ExcludeFromDescription();
        return app;
    }


    private static async Task<RedirectHttpResult> CompleteSignIn(
        string? state,
        string? code,
        string? iss,
        string? error,
        HttpContext context,
        ToolSignIns signIns)
    {
        var finish = await signIns.Complete(context.User.GetUserId(), state, code, iss, refused: !string.IsNullOrEmpty(error), context.RequestAborted);

        // Only words this server chose go into the address: nothing the provider sent is echoed.
        var outcome = finish.Connected ? "connected" : "failed";
        return TypedResults.LocalRedirect(
            finish is { SpaceId: { } spaceId, ConnectionId: { } connectionId }
                ? $"/?tool-sign-in={outcome}&space={spaceId}&connection={connectionId}"
                : $"/?tool-sign-in={outcome}");
    }

    /// <summary>
    /// Who this server is, for a tool server that reads a client document instead of registering clients. Public
    /// by nature, and holds no secret: this server signs in as a public client with a proof key.
    /// </summary>
    private static Results<JsonHttpResult<Dictionary<string, object>>, NotFound> DescribeClient(IOptions<ToolSignInOptions> options) =>
        options.Value is { ClientDocumentUrl: { } document, RedirectUrl: { } redirect }
            ? TypedResults.Json(
                new Dictionary<string, object>
                {
                    ["client_id"] = document.ToString(),
                    ["client_name"] = "Drover",
                    ["redirect_uris"] = new[] { redirect.ToString() },
                    ["grant_types"] = clientGrantTypes,
                    ["response_types"] = clientResponseTypes,
                    ["token_endpoint_auth_method"] = "none",
                })
            : TypedResults.NotFound();

    private static async Task<Ok<List<ToolConnectionSummary>>> ListConnections(HttpContext context, ToolConnectionService connections)
    {
        var listed = await connections.ListConnections(context.RequestAborted);
        var signIns = await connections.ListSignIns(listed, context.RequestAborted);
        return TypedResults.Ok(listed.Select(connection => connection.ToSummary(SignInOf(signIns, connection))).ToList());
    }

    private static async Task<Results<Ok<ToolSignInStart>, NotFound, ForbidHttpResult>> BeginSignIn(
        Guid connectionId,
        HttpContext context,
        ToolConnectionService connections,
        ToolSignIns signIns)
    {
        var access = context.GetSpaceAccess();
        var connection = await connections.FindConnection(connectionId, context.RequestAborted);

        if (connection is null)
        {
            return TypedResults.NotFound();
        }

        // One sign-in for the whole space makes every bot run in it act as that account: an admin's decision.
        if (connection.CredentialScope is CredentialScope.Space && access.Role > MembershipRole.Admin)
        {
            return TypedResults.Forbid();
        }

        var start = await signIns.Begin(access.SpaceId, access.UserId, connectionId, context.RequestAborted);
        return start.Step is SignInStep.NotFound
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ToolSignInStart(start.Step.ToString().ToLowerInvariant(), start.AuthorizationUrl?.ToString(), start.Problem));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> SignOut(Guid connectionId, HttpContext context, ToolConnectionService connections)
    {
        var connection = await connections.FindConnection(connectionId, context.RequestAborted);

        if (connection is null)
        {
            return TypedResults.NotFound();
        }

        if (connection.CredentialScope is CredentialScope.Space && context.GetSpaceAccess().Role > MembershipRole.Admin)
        {
            return TypedResults.Forbid();
        }

        return await connections.SignOut(connectionId, context.RequestAborted) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

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
            || !TryRead<ProtocolMode>(request.ProtocolMode, ProtocolMode.Automatic, out var mode)
            || !TryRead<CredentialScope>(request.SignInScope, CredentialScope.PerUser, out var credentialScope))
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
                ClientId = request.ClientId,
                ClientSecret = request.ClientSecret,
                Issuer = request.Issuer,
                CredentialScope = credentialScope,
            },
            context.RequestAborted);

        if (change.Connection is not { } connection)
        {
            return TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
        }

        var location = $"/api/spaces/{connection.SpaceId}/tool-connections/{connection.Id}/tools";

        if (connection.AuthKind is ToolAuthKind.Oauth)
        {
            // Nothing can be listed before somebody has signed in, and nobody has.
            return TypedResults.Created(
                location,
                new ToolConnectionRefresh(connection.ToSummary(), [], "Sign in to this connection to list its tools.", 0, 0, 0, 0));
        }

        // The connection is saved whatever the server answers; the answer says whether its tools could be listed.
        var listed = await connections.Refresh(connection.Id, context.RequestAborted);
        return TypedResults.Created(
            location,
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
        return change.Connection is { } connection
            ? TypedResults.Ok(connection.ToSummary(SignInOf(await connections.ListSignIns([connection], context.RequestAborted), connection)))
            : TypedResults.NotFound();
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
            ? TypedResults.Ok(listed.ToResponse(SignInOf(await connections.ListSignIns([listed.Connection], context.RequestAborted), listed.Connection)))
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

    private static CredentialStatus? SignInOf(Dictionary<Guid, CredentialStatus> signIns, ToolConnection connection) =>
        signIns.TryGetValue(connection.Id, out var status) ? status : null;

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
