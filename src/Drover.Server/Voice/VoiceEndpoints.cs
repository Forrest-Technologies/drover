namespace Drover.Server.Voice;

/// <summary>
/// Speech in a space: what can listen and speak, the space's choices, writing down a recording and reading text
/// aloud. None of it sits under the tenant scope filter, because most of it waits on a speech provider; the service
/// opens its own short transactions as the person who asked. A recording is the raw request body and goes to the
/// provider through memory only: it is never written to disk, to the database, or to a log.
/// </summary>
public static class VoiceEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapVoiceEndpoints(this RouteGroupBuilder space)
    {
        var rules = ((IEndpointRouteBuilder)space).ServiceProvider.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules;
        var voice = space.MapGroup("/voice");
        voice.MapGet("/options", GetOptions)
            .WithName("GetVoiceOptions")
            .WithSummary("The models that can write down speech, read aloud and hold a live call in this space, the one each uses now, and the server's limits.");
        voice.MapPut("/settings", SaveSettings)
            .RequireSpaceRole(MembershipRole.Admin)
            .WithName("SaveVoiceSettings")
            .WithSummary("Saves which model listens, speaks and holds live calls. A slot left null is chosen by the server.");
        voice.MapGet("/voices", ListVoices)
            .WithName("ListVoices")
            .WithSummary("The voices a speech model offers. Leave connectionId out for the server's own speech service.");
        voice.MapPost("/transcribe", Transcribe)
            .WithBodyLimit(rules.MaxVoiceRecordingBytes, _ => VoiceService.RecordingTooLarge(rules.MaxVoiceRecordingBytes))
            .WithName("TranscribeRecording")
            .WithSummary("Writes down the recording in the request body, whose Content-Type names its audio format. Nothing of it is stored.");
        voice.MapPost("/speak", Speak)
            .WithName("SpeakText")
            .WithSummary("Reads a message's text, or a sample text, aloud and streams the audio as it is made.");
        return space;
    }

    /// <summary>The status a refusal answers with, and its sentence as the problem's title.</summary>
    public static ProblemHttpResult ToProblem(this VoiceProblem problem) =>
        TypedResults.Problem(
            title: problem.Message,
            statusCode: problem.Kind switch
            {
                VoiceProblemKind.Invalid => StatusCodes.Status400BadRequest,
                VoiceProblemKind.NotFound => StatusCodes.Status404NotFound,
                VoiceProblemKind.TooLarge => StatusCodes.Status413PayloadTooLarge,
                VoiceProblemKind.NotReady => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status502BadGateway,
            });

    #endregion

    #region Endpoints

    private static async Task<Ok<VoiceOptionsResponse>> GetOptions(HttpContext context, VoiceService voice)
    {
        var access = context.GetSpaceAccess();
        return TypedResults.Ok((await voice.GetOptions(access.SpaceId, access.UserId, context.RequestAborted)).ToResponse());
    }

    private static async Task<Results<Ok<VoiceOptionsResponse>, ProblemHttpResult>> SaveSettings(
        VoiceSettingsRequest request,
        HttpContext context,
        VoiceService voice)
    {
        var access = context.GetSpaceAccess();
        var settings = new VoiceSettings { Listen = ToChoice(request.Listen), Speak = ToChoice(request.Speak), Live = ToChoice(request.Live) };
        var saved = await voice.SaveSettings(access.SpaceId, access.UserId, settings, context.RequestAborted);
        return saved.Value is { } options ? TypedResults.Ok(options.ToResponse()) : saved.Problem!.ToProblem();
    }

    private static async Task<Results<Ok<List<VoiceOptionResponse>>, ProblemHttpResult>> ListVoices(
        Guid? connectionId,
        string? model,
        HttpContext context,
        VoiceService voice)
    {
        var access = context.GetSpaceAccess();
        var listed = await voice.ListVoices(access.SpaceId, access.UserId, connectionId, model ?? string.Empty, context.RequestAborted);
        return listed.Value is { } voices
            ? TypedResults.Ok(voices.Select(option => new VoiceOptionResponse(option.Id, option.Name)).ToList())
            : listed.Problem!.ToProblem();
    }

    private static async Task<Results<Ok<TranscriptResponse>, ProblemHttpResult>> Transcribe(
        HttpContext context,
        VoiceService voice,
        IOptions<DeploymentPolicyOptions> deployment)
    {
        var access = context.GetSpaceAccess();

        try
        {
            var heard = await voice.Transcribe(access.SpaceId, access.UserId, context.Request.Body, context.Request.ContentType, context.RequestAborted);
            return heard.Value is { } text ? TypedResults.Ok(new TranscriptResponse(text)) : heard.Problem!.ToProblem();
        }
        catch (BadHttpRequestException tooLarge) when (tooLarge.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // A body sent without a declared length is cut off by the server itself at the same limit.
            return TypedResults.Problem(
                title: VoiceService.RecordingTooLarge(deployment.Value.Rules.MaxVoiceRecordingBytes),
                detail: tooLarge.GetType().Name,
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
    }

    /// <summary>
    /// Streams the audio as the provider makes it. Nothing is sent until the first of it arrives, so a provider that
    /// refuses still gets a problem as its answer. One that fails part way has its answer cut off, which a player
    /// hears as the end.
    /// </summary>
    private static async Task Speak(SpeakRequest request, HttpContext context, VoiceService voice)
    {
        var access = context.GetSpaceAccess();
        var prepared = await voice.PrepareSpeech(
            access.SpaceId,
            access.UserId,
            request.MessageId,
            request.Text,
            request.Voice,
            request.Format,
            context.RequestAborted);

        if (prepared.Value is not { } plan)
        {
            await prepared.Problem!.ToProblem().ExecuteAsync(context);
            return;
        }

        var failure = await voice.Speak(
            plan,
            async (audio, cancellationToken) =>
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.ContentType = plan.MediaType;
                    context.Response.Headers.CacheControl = "no-store";
                }

                await context.Response.Body.WriteAsync(audio, cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            },
            context.RequestAborted);

        if (failure is null)
        {
            return;
        }

        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        await failure.ToProblem().ExecuteAsync(context);
    }

    #endregion

    #region Mapping

    private static VoiceChoice? ToChoice(VoiceChoiceBody? body) =>
        body is null ? null : new VoiceChoice(body.ConnectionId, body.Model ?? string.Empty, body.Voice);

    private static VoiceOptionsResponse ToResponse(this VoiceOptionsView view) =>
        new(
            view.Listen.ToResponse(),
            view.Speak.ToResponse(),
            view.Live.ToResponse(),
            new VoiceLimits(Limit(view.MaxRecordingBytes), Limit(view.MaxReadAloudCharacters), Limit(view.MaxCallMinutes)));

    private static int? Limit(int value) => value == int.MaxValue ? null : value;

    private static VoiceSlotResponse ToResponse(this VoiceSlotView slot) =>
        new(
            [.. slot.Candidates.Select(candidate => new VoiceSourceResponse(
                candidate.ConnectionId,
                candidate.ConnectionName,
                candidate.Model,
                candidate.IsServer,
                VoiceChoices.LooksPrivate(candidate.Address)))],
            slot.Pick is { } pick
                ? new VoiceSelection(
                    pick.Candidate.ConnectionId,
                    pick.Candidate.ConnectionName,
                    pick.Candidate.Model,
                    pick.Voice,
                    pick.Candidate.IsServer,
                    VoiceChoices.LooksPrivate(pick.Candidate.Address),
                    pick.Automatic)
                : null);

    #endregion
}
