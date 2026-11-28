namespace Drover.Server.Models;

/// <summary>
/// Model connections of a space. Creating one, replacing its key, and the reads run under the tenant scope filter.
/// Testing and refreshing wait on the provider, so they sit outside the filter and the service opens its own short
/// scopes around the call. No response carries a key: a connection says <c>HasKey</c> and nothing more.
/// </summary>
public static class ModelConnectionEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapModelConnectionEndpoints(this RouteGroupBuilder space)
    {
        var scoped = space.MapGroup("/model-connections").AddEndpointFilter<TenantScopeFilter>();
        scoped.MapGet("/", ListConnections).WithName("ListModelConnections");
        scoped.MapPost("/", CreateConnection).RequireSpaceRole(MembershipRole.Admin).WithName("CreateModelConnection");
        scoped.MapPut("/{connectionId:guid}/key", ReplaceKey).RequireSpaceRole(MembershipRole.Admin).WithName("ReplaceModelKey");
        scoped.MapGet("/{connectionId:guid}/models", ListModels).WithName("ListModels");
        scoped.MapPut("/{connectionId:guid}/models/vision", SetModelVision).RequireSpaceRole(MembershipRole.Admin).WithName("SetModelVision");
        scoped.MapPut("/{connectionId:guid}/models/voice", SetModelVoice).RequireSpaceRole(MembershipRole.Admin).WithName("SetModelVoice");
        scoped.MapPut("/{connectionId:guid}/model-settings", SetModelSettings).RequireSpaceRole(MembershipRole.Admin).WithName("SetModelSettings");
        scoped.MapDelete("/{connectionId:guid}/model-settings", ClearModelSettings).RequireSpaceRole(MembershipRole.Admin).WithName("ClearModelSettings");

        var calling = space.MapGroup("/model-connections");
        calling.MapPost("/{connectionId:guid}/test", TestConnection).RequireSpaceRole(MembershipRole.Admin).WithName("TestModelConnection");
        calling.MapPost("/{connectionId:guid}/models/refresh", RefreshModels).RequireSpaceRole(MembershipRole.Admin).WithName("RefreshModels");

        // A person's own sign-in. Each waits on the provider, so none sits under the scope filter: the services open
        // their own short scopes as the caller. Any member may sign in; only the person who started a sign-in can
        // finish or read it, and only their own sign-in can be removed.
        calling.MapPost("/sign-in", StartSignIn).WithName("StartModelSignIn");
        calling.MapPost("/sign-in/{attemptId:guid}/finish", FinishSignIn).WithName("FinishModelSignIn");
        calling.MapGet("/sign-in/{attemptId:guid}", ReadSignIn).WithName("ReadModelSignIn");
        calling.MapDelete("/sign-in/{attemptId:guid}", CancelSignIn).WithName("CancelModelSignIn");
        calling.MapDelete("/{connectionId:guid}/sign-in", SignOut).WithName("SignOutOfModelConnection");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Ok<List<ModelConnectionSummary>>> ListConnections(HttpContext context, ModelConnectionService models)
    {
        var listed = await models.ListConnections(context.RequestAborted);
        var own = await models.ListOwnSignIns(listed, context.RequestAborted);
        return TypedResults.Ok(listed.Select(connection => connection.ToSummary(ViewOf(connection, own))).ToList());
    }

    private static async Task<Results<Created<ModelConnectionSummary>, ProblemHttpResult>> CreateConnection(
        CreateModelConnectionRequest request,
        HttpContext context,
        ModelConnectionService models)
    {
        var change = await models.Create(
            request.Kind ?? string.Empty,
            request.Name ?? string.Empty,
            request.BaseUrl,
            request.ApiKey,
            request.DefaultModel,
            context.RequestAborted);

        return change.Connection is { } connection
            ? TypedResults.Created($"/api/spaces/{connection.SpaceId}/model-connections", connection.ToSummary())
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ModelConnectionSummary>, NotFound, ProblemHttpResult>> ReplaceKey(
        Guid connectionId,
        UpdateModelKeyRequest request,
        HttpContext context,
        ModelConnectionService models)
    {
        var change = await models.ReplaceKey(connectionId, request.ApiKey ?? string.Empty, context.RequestAborted);

        if (change.Connection is { } connection)
        {
            return TypedResults.Ok(connection.ToSummary());
        }

        return change.Problem is null
            ? TypedResults.NotFound()
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<List<ModelCatalogResponse>>, NotFound>> ListModels(
        Guid connectionId,
        HttpContext context,
        ModelConnectionService models)
    {
        var catalog = await models.ListModels(connectionId, context.RequestAborted);
        return catalog is null ? TypedResults.NotFound() : TypedResults.Ok(catalog.Select(entry => entry.ToResponse()).ToList());
    }

    private static async Task<Results<Ok<ModelCatalogResponse>, NotFound, ProblemHttpResult>> SetModelVision(
        Guid connectionId,
        SetModelVisionRequest request,
        HttpContext context,
        ModelConnectionService models)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return TypedResults.Problem(title: "Name the model.", statusCode: StatusCodes.Status400BadRequest);
        }

        var change = await models.SetVision(connectionId, request.Model, request.ReadsImages, context.RequestAborted);

        if (change is null)
        {
            return TypedResults.NotFound();
        }

        return change.Entry is { } entry
            ? TypedResults.Ok(entry.ToResponse())
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ModelCatalogResponse>, NotFound, ProblemHttpResult>> SetModelSettings(
        Guid connectionId,
        SetModelSettingsRequest request,
        HttpContext context,
        ModelConnectionService models)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return TypedResults.Problem(title: "Name the model.", statusCode: StatusCodes.Status400BadRequest);
        }

        var prices = request.Pricing;
        var change = await models.SetSettings(
            connectionId,
            request.Model,
            request.Capabilities.ToOverrides(),
            new ModelPricing
            {
                InputPerMillion = prices?.InputPerMillion,
                OutputPerMillion = prices?.OutputPerMillion,
                CachedInputPerMillion = prices?.CachedInputPerMillion,
            },
            prices?.Currency,
            context.GetSpaceAccess().UserId,
            context.RequestAborted);

        if (change is null)
        {
            return TypedResults.NotFound();
        }

        return change.Entry is { } entry
            ? TypedResults.Ok(entry.ToResponse())
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ClearModelSettings(
        Guid connectionId,
        string? model,
        HttpContext context,
        ModelConnectionService models)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return TypedResults.Problem(title: "Name the model.", statusCode: StatusCodes.Status400BadRequest);
        }

        return await models.ClearSettings(connectionId, model, context.GetSpaceAccess().UserId, context.RequestAborted) is null
            ? TypedResults.NotFound()
            : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ModelCatalogResponse>, NotFound, ProblemHttpResult>> SetModelVoice(
        Guid connectionId,
        SetModelVoiceRequest request,
        HttpContext context,
        ModelConnectionService models)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return TypedResults.Problem(title: "Name the model.", statusCode: StatusCodes.Status400BadRequest);
        }

        var change = await models.SetVoice(
            connectionId,
            request.Model,
            request.SpeechToText,
            request.TextToSpeech,
            request.RealtimeVoice,
            context.RequestAborted);

        if (change is null)
        {
            return TypedResults.NotFound();
        }

        return change.Entry is { } entry
            ? TypedResults.Ok(entry.ToResponse())
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ConnectionTestResponse>, NotFound>> TestConnection(
        Guid connectionId,
        HttpContext context,
        ModelConnectionService models)
    {
        var check = await models.Test(connectionId, context.RequestAborted);

        if (check is null)
        {
            return TypedResults.NotFound();
        }

        var own = await models.ReadOwnSignIns([check.Connection], context.RequestAborted);
        return TypedResults.Ok(new ConnectionTestResponse(
            check.Result?.Succeeded ?? false,
            check.Message,
            check.Failure?.ToWireName(),
            check.Connection.ToSummary(ViewOf(check.Connection, own))));
    }

    private static async Task<Results<Ok<ModelRefreshResponse>, NotFound>> RefreshModels(
        Guid connectionId,
        HttpContext context,
        ModelConnectionService models)
    {
        var check = await models.RefreshModels(connectionId, context.RequestAborted);

        return check is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ModelRefreshResponse(
                check.Failure is null,
                check.Message,
                check.Failure?.ToWireName(),
                [.. check.Catalog.Select(entry => entry.ToResponse())]));
    }

    private static async Task<Results<Ok<ModelSignInStarted>, ProblemHttpResult>> StartSignIn(
        StartModelSignInRequest request,
        HttpContext context,
        ModelSignIns signIns)
    {
        Uri? redirect = null;

        if (request.RedirectUri is { Length: > 0 } given && !Uri.TryCreate(given, UriKind.Absolute, out redirect))
        {
            return TypedResults.Problem(title: "That return address is not an address.", statusCode: StatusCodes.Status400BadRequest);
        }

        var access = context.GetSpaceAccess();
        var begun = await signIns.Begin(access.SpaceId, access.UserId, request.Kind ?? string.Empty, redirect, context.RequestAborted);

        return begun.Address is { } address
            ? TypedResults.Ok(new ModelSignInStarted(begun.AttemptId, address.AbsoluteUri, ToWireName(begun.Wait), begun.Code))
            : TypedResults.Problem(title: begun.Problem, statusCode: begun.Status);
    }

    private static async Task<Results<Ok<ModelConnectionSummary>, NotFound, ProblemHttpResult>> FinishSignIn(
        Guid attemptId,
        FinishModelSignInRequest request,
        HttpContext context,
        ModelSignIns signIns,
        RunScopes scopes)
    {
        var access = context.GetSpaceAccess();

        // A browser's return is a short query string; one this long is not a sign-in's, and is not passed on.
        if (request.Query?.Length > FinishModelSignInRequest.MostLength)
        {
            return TypedResults.Problem(title: "That return is too long to be a sign-in's.", statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = await signIns.Finish(access.SpaceId, access.UserId, attemptId, request.Query, context.RequestAborted);

        if (outcome.State is null)
        {
            return TypedResults.NotFound();
        }

        if (outcome.ConnectionId is not { } connectionId)
        {
            return TypedResults.Problem(title: outcome.Problem ?? "Sign-in was cancelled.", statusCode: outcome.Status);
        }

        return await Summarize(scopes, access, connectionId, context.RequestAborted) is { } summary
            ? TypedResults.Ok(summary)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ModelSignInProgress>, NotFound>> ReadSignIn(Guid attemptId, HttpContext context, ModelSignIns signIns, RunScopes scopes)
    {
        var access = context.GetSpaceAccess();
        var outcome = signIns.Read(access.SpaceId, access.UserId, attemptId);

        if (outcome.State is not { } state)
        {
            return TypedResults.NotFound();
        }

        var connection = outcome.ConnectionId is { } connectionId ? await Summarize(scopes, access, connectionId, context.RequestAborted) : null;
        return TypedResults.Ok(new ModelSignInProgress(ToWireName(state), connection, outcome.Problem));
    }

    private static Results<NoContent, NotFound, ProblemHttpResult> CancelSignIn(
        Guid attemptId, HttpContext context, ModelSignIns signIns)
    {
        var access = context.GetSpaceAccess();
        return signIns.CancelPoll(access.SpaceId, access.UserId, attemptId) switch
        {
            204 => TypedResults.NoContent(),
            404 => TypedResults.NotFound(),
            _ => TypedResults.Problem(
                title: "This sign-in already ended or is finishing. Check Models for the result.",
                statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<Results<NoContent, NotFound>> SignOut(Guid connectionId, HttpContext context, ModelSignIns signIns)
    {
        var access = context.GetSpaceAccess();
        return await signIns.SignOut(access.SpaceId, access.UserId, connectionId, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    #endregion

    #region Private Methods

    private static SignInView? ViewOf(ModelConnection connection, Dictionary<Guid, ModelConnectionCredential?> own) =>
        own.TryGetValue(connection.Id, out var credential) ? new SignInView(credential) : null;

    /// <summary>The connection as the caller sees it, read in a short scope of their own, or null when it is gone.</summary>
    private static Task<ModelConnectionSummary?> Summarize(RunScopes scopes, SpaceAccess access, Guid connectionId, CancellationToken cancellationToken) =>
        scopes.For(access.SpaceId, access.UserId).Transact(
            async services =>
            {
                var models = services.GetRequiredService<ModelConnectionService>();

                if (await services.GetRequiredService<ModelConnectionRepository>().FindConnection(connectionId, cancellationToken) is not { } connection)
                {
                    return null;
                }

                var own = await models.ListOwnSignIns([connection], cancellationToken);
                return connection.ToSummary(ViewOf(connection, own));
            },
            cancellationToken);

    private static string ToWireName(ModelSignInWait wait) =>
        wait switch
        {
            ModelSignInWait.Paste => ModelSignInWaits.Paste,
            ModelSignInWait.Poll => ModelSignInWaits.Poll,
            _ => ModelSignInWaits.Return,
        };

    private static string ToWireName(ModelSignInState state) =>
        state switch
        {
            ModelSignInState.Done => ModelSignInStatuses.Done,
            ModelSignInState.Refused => ModelSignInStatuses.Refused,
            ModelSignInState.Expired => ModelSignInStatuses.Expired,
            _ => ModelSignInStatuses.Waiting,
        };

    #endregion
}
