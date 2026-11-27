namespace Drover.Server.Voice;

/// <summary>
/// A live call with a conversation's bot, as one WebSocket. It is authorized like sending a message there: the
/// same sign-in, the same membership check, and a conversation the caller can read. A WebSocket handshake is a GET,
/// which carries no antiforgery token, so a browser's request must come from this server's own origin; a native
/// client sends no <c>Origin</c> and is let through on its bearer session. Everything that can refuse the call does
/// so before the upgrade, as a problem with a sentence. No tenant transaction is ever open while the socket is.
/// </summary>
public static class VoiceCallEndpoint
{
    #region Public Methods

    public static IEndpointRouteBuilder MapVoiceCallEndpoint(this RouteGroupBuilder space)
    {
        space.MapGet("/threads/{threadId:guid}/call", Call)
            .WithName("CallBot")
            .WithSummary("A live spoken conversation with the thread's bot, as a WebSocket. Binary frames are audio; text frames are JSON.");
        return space;
    }

    /// <summary>
    /// Whether a request may open a socket here as far as its <c>Origin</c> says: none at all (not a browser), this
    /// server's own, or the public address the operator configured.
    /// </summary>
    public static bool IsOwnOrigin(HttpRequest request, Uri? publicAddress)
    {
        var origin = request.Headers.Origin.ToString();

        if (origin.Length == 0)
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var from)
            && (SameOrigin(from, new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? -1).Uri)
                || (publicAddress is not null && SameOrigin(from, publicAddress)));
    }

    #endregion

    #region Endpoints

    private static async Task Call(
        Guid threadId,
        HttpContext context,
        VoiceService voice,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime,
        TimeProvider time,
        ILoggerFactory loggers)
    {
        if (!IsOwnOrigin(context.Request, PublicAddress.Read(configuration)))
        {
            await TypedResults.Problem(title: "A live call can only be opened from Drover itself.", statusCode: StatusCodes.Status403Forbidden)
                .ExecuteAsync(context);
            return;
        }

        var access = context.GetSpaceAccess();
        var prepared = await voice.PrepareCall(access.SpaceId, access.UserId, threadId, context.RequestAborted);

        if (prepared.Value is not { } plan)
        {
            await prepared.Problem!.ToProblem().ExecuteAsync(context);
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            // A plain request that got this far would have been let in: a client asks this way first, to read a
            // refusal's sentence, which a WebSocket handshake cannot carry back to it.
            await TypedResults.Problem(title: "Open a live call as a WebSocket.", statusCode: StatusCodes.Status426UpgradeRequired).ExecuteAsync(context);
            return;
        }

        // The provider is reached before the upgrade, so a call it refuses is still answered with a problem.
        var opened = await voice.OpenCall(plan, context.RequestAborted);

        if (opened.Value is not { } session)
        {
            await opened.Problem!.ToProblem().ExecuteAsync(context);
            return;
        }

        await using (session)
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await using var guard = SessionGuard.Start(context);
            using var ending = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping, guard.Ended);
            var call = new VoiceCallSession(plan, session, socket, voice, time, loggers.CreateLogger(typeof(VoiceCallEndpoint)));
            await call.Run(lifetime.ApplicationStopping, ending.Token);
        }
    }

    #endregion

    #region Private Methods

    private static bool SameOrigin(Uri one, Uri other) =>
        string.Equals(one.Scheme, other.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(one.IdnHost, other.IdnHost, StringComparison.OrdinalIgnoreCase)
        && one.Port == other.Port;

    #endregion
}
