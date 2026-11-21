namespace Drover.Server.Push;

/// <summary>
/// A person's devices and what a woken one shows. A device is the person's in every space, so these sit outside the
/// space routes. The wake text is asked for by the device's service worker with the person's own sign-in: it names
/// only threads that person can see.
/// </summary>
public static class PushEndpoints
{
    #region Public Methods

    /// <summary>The header in which a browser names the subscription that woke it, when it asks what to show.</summary>
    public const string PushEndpointHeader = "Push-Endpoint";

    public static IEndpointRouteBuilder MapPushEndpoints(this RouteGroupBuilder api)
    {
        var push = api.MapGroup("/push").RequireAuthorization();
        push.MapGet("/key", GetKey).WithName("GetPushKey")
            .WithSummary("The public key a device subscribes with, or why this server cannot send notifications.");
        push.MapPut("/device", TurnOn).WithName("TurnOnPush")
            .WithSummary("Wakes this device for the signed-in person from now on.");
        push.MapPost("/device/state", GetDeviceState).WithName("GetPushDeviceState")
            .WithSummary("Whether this device is woken for the signed-in person.");
        push.MapPost("/device/off", TurnOff).WithName("TurnOffPush")
            .WithSummary("Stops waking this device for the signed-in person.");
        push.MapPost("/device/test", TestDevice)
            .RequireRateLimiting(SecurityServiceExtensions.PushTestRateLimit)
            .WithBodyLimit(RequestLimits.Account)
            .WithName("TestPushDevice");
        push.MapGet("/wake", GetWakeText).WithName("GetPushWakeText")
            .WithSummary("What a woken device shows: the bot and what happened, for a thread the person can see. A browser sends its own subscription address in the Push-Endpoint header and the person the wake was sent for in the user query value: it is answered only while the signed-in person holds a subscription for that address (403, checked first) and is that person (409 otherwise, also when a browser names nobody).");
        return api;
    }

    /// <summary>Whether a bot may notify the signed-in person, under the space routes.</summary>
    public static IEndpointRouteBuilder MapBotNotifyEndpoints(this RouteGroupBuilder space)
    {
        space.MapGet("/bots/{botId:guid}/notify", GetBotNotify).WithName("GetBotNotify");
        space.MapPut("/bots/{botId:guid}/notify", SetBotNotify).WithName("SetBotNotify")
            .WithSummary("Lets the bot notify the signed-in person, or stops it. A choice of theirs alone.");
        return space;
    }

    #endregion

    #region Endpoints

    private static Ok<PushKeyResponse> GetKey(PushAvailability availability) =>
        TypedResults.Ok(new PushKeyResponse(availability.PublicKey, availability.Problem));

    private static async Task<Results<NoContent, ValidationProblem>> TurnOn(PushDeviceRequest device, HttpContext context, PushService push) =>
        await push.TurnOn(context.User.GetUserId(), device, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(PushDeviceRequest.Endpoint)] = ["This is not a push subscription the server can use."],
            });

    private static async Task<Ok<PushDeviceState>> GetDeviceState(PushDeviceQuery device, HttpContext context, PushService push) =>
        TypedResults.Ok(new PushDeviceState(await push.IsOn(context.User.GetUserId(), device.Endpoint, context.RequestAborted)));

    private static async Task<NoContent> TurnOff(PushDeviceQuery device, HttpContext context, PushService push)
    {
        await push.TurnOff(context.User.GetUserId(), device.Endpoint, context.RequestAborted);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> TestDevice(
        PushDeviceQuery device,
        HttpContext context,
        PushService push) =>
        (await push.Test(context.User.GetUserId(), device.Endpoint, context.RequestAborted)) switch
        {
            PushOutcome.Delivered => TypedResults.NoContent(),
            null => TypedResults.NotFound(),
            PushOutcome.SubscriptionGone => TypedResults.Problem(
                title: "This device’s subscription expired. Turn notifications off and on to reconnect.",
                statusCode: StatusCodes.Status410Gone),
            _ => TypedResults.Problem(
                title: "The notification distributor could not accept the test.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
        };

    // A browser that was left subscribed by one person is woken for that person's runs. The worker names the
    // subscription it holds, in a header so the address, which anyone holding it may push to, is not in a URL or a log
    // line, and the person the wake was sent for, which the wake itself carries. Only that person is answered, and
    // only while they hold a subscription for that address. So the next person on the browser is told nothing of the
    // last one's bots, and neither is the next person when a wake for the last one was already queued as they
    // switched. The two refusals differ: 403 says the signed-in person holds no subscription for this address (nothing
    // is shown, and the worker ends it), and is checked first; 409 says the wake is for someone else, or names nobody
    // (nothing is shown, the browser's own subscription is the signed-in person's and is kept). The Android app and the desktop's live signal ask
    // without either: each checks for itself that the account is the one it was registered for.
    private static async Task<Results<Ok<PushWakeText>, NotFound, StatusCodeHttpResult>> GetWakeText(
        string kind,
        Guid thread,
        Guid? user,
        HttpContext context,
        PushService push)
    {
        var userId = context.User.GetUserId();
        var fromBrowser = context.Request.Headers.TryGetValue(PushEndpointHeader, out var named);

        // Ownership of the subscription is checked first. A header that is there but blank names no subscription, so it
        // is not one the person holds. When the person signed in holds no subscription for the address, this browser's
        // subscription is someone else's, whoever the wake was for, and the worker ends it. The person the wake was sent
        // for is compared only once the subscription is the signed-in person's.
        if (fromBrowser && !await push.IsOn(userId, named.ToString().Trim(), context.RequestAborted))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        // A browser's wake that names nobody (queued before the person was added to the payload) cannot be shown to
        // the person signed in: nothing says it was for them. The Android app and the desktop send no subscription
        // address and are not bound this way.
        if ((fromBrowser && user is null) || (user is { } intended && intended != userId))
        {
            return TypedResults.StatusCode(StatusCodes.Status409Conflict);
        }

        return await push.Describe(userId, kind, thread, context.RequestAborted) is { } text
            ? TypedResults.Ok(text)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<BotNotifySetting>, NotFound>> GetBotNotify(Guid botId, HttpContext context, PushService push)
    {
        var access = context.GetSpaceAccess();
        return await push.Notifies(access.SpaceId, botId, access.UserId, context.RequestAborted) is { } notifies
            ? TypedResults.Ok(new BotNotifySetting(notifies))
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<BotNotifySetting>, NotFound>> SetBotNotify(Guid botId, BotNotifySetting setting, HttpContext context, PushService push)
    {
        var access = context.GetSpaceAccess();
        return await push.SetNotifies(access.SpaceId, botId, access.UserId, setting.Notifies, context.RequestAborted)
            ? TypedResults.Ok(setting)
            : TypedResults.NotFound();
    }

    #endregion
}
