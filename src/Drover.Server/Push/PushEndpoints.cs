namespace Drover.Server.Push;

/// <summary>
/// A person's devices and what a woken one shows. A device is the person's in every space, so these sit outside the
/// space routes. The wake text is asked for by the device's service worker with the person's own sign-in: it names
/// only threads that person can see.
/// </summary>
public static class PushEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapPushEndpoints(this RouteGroupBuilder api)
    {
        var push = api.MapGroup("/push").RequireAuthorization();
        push.MapGet("/key", GetKey).WithName("GetPushKey")
            .WithSummary("The public key a device subscribes with, or why this server cannot send notifications.");
        push.MapPut("/device", TurnOn).WithName("TurnOnPush")
            .WithSummary("Wakes this device for the signed-in person from now on.");
        push.MapPost("/device/move", Move).WithName("MovePushDevice")
            .WithSummary("Puts a device's new subscription in the place of its previous one, when that one was the signed-in person's.");
        push.MapPost("/device/state", GetDeviceState).WithName("GetPushDeviceState")
            .WithSummary("Whether this device is woken for the signed-in person.");
        push.MapPost("/device/off", TurnOff).WithName("TurnOffPush")
            .WithSummary("Stops waking this device for the signed-in person.");
        push.MapPost("/device/test", TestDevice)
            .RequireRateLimiting(SecurityServiceExtensions.PushTestRateLimit)
            .WithBodyLimit(RequestLimits.Account)
            .WithName("TestPushDevice");
        push.MapGet("/wake", GetWakeText).WithName("GetPushWakeText")
            .WithSummary("What a woken device shows: the bot and what happened, for a thread the person can see.");
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

    /// <summary>
    /// A previous subscription that is not the signed-in person's is answered as missing: the request learns nothing
    /// about whose it was, and nothing is written.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Move(MovePushDeviceRequest move, HttpContext context, PushService push) =>
        await push.Move(context.User.GetUserId(), move.PreviousEndpoint, move.Device, context.RequestAborted) switch
        {
            PushMove.Moved => TypedResults.NoContent(),
            PushMove.NotThePersons => TypedResults.NotFound(),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(PushDeviceRequest.Endpoint)] = ["This is not a push subscription the server can use."],
            }),
        };

    private static async Task<Ok<PushDeviceState>> GetDeviceState(PushDeviceQuery device, HttpContext context, PushService push)
    {
        var userId = context.User.GetUserId();
        return TypedResults.Ok(new PushDeviceState(await push.IsOn(userId, device.Endpoint, context.RequestAborted), userId));
    }

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

    private static async Task<Results<Ok<PushWakeText>, NotFound>> GetWakeText(string kind, Guid thread, HttpContext context, PushService push) =>
        await push.Describe(context.User.GetUserId(), kind, thread, context.RequestAborted) is { } text
            ? TypedResults.Ok(text)
            : TypedResults.NotFound();

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
