namespace Drover.Engine.Voice;

/// <summary>
/// Speech for a space: writing down a recording, reading text aloud, and the data side of a live call. Every call to
/// a speech provider waits on the network, so nothing here runs in a request's tenant transaction. Each method
/// opens short transactions of its own as the person who asked: read what is needed (the choice, the key, the
/// space's outbound rule) and close, call the provider, and open another only to write. Audio and the words in it
/// are never stored by this service and never logged; a log line holds ids, counts and durations.
/// </summary>
public sealed class VoiceService(
    RunScopes scopes,
    IEnumerable<ISpeechToText> listeners,
    IEnumerable<ITextToSpeech> speakers,
    IEnumerable<IRealtimeVoice> callers,
    ServerVoiceOptions server,
    IOptions<DeploymentPolicyOptions> deployment,
    UserSignalHub signals,
    RunEventFeed feed,
    TimeProvider time,
    ILogger<VoiceService> logger)
{
    #region Private Fields

    public const string SpokenReplyInstruction =
        "The person is hearing this reply, not reading it. Answer in short plain sentences. No lists, tables, code or links unless asked.";

    public const string BotIsWorking = "The bot is still working in this conversation. Call when it has finished.";

    public const int CallHistoryMessages = 20;

    /// <summary>What the voice of a call is told when it was handed work and the person had said nothing to do.</summary>
    public const string NothingAsked = "Nothing the person asked for was heard. Ask them to say it again.";

    public const string WorkNotSent = "The request could not be given to the bot. Ask the person to try again.";

    public const string WaitingForApproval =
        "The bot needs the person's approval before it goes on. The request is in the conversation on their screen.";

    public const string WaitingForTakeover =
        "The bot needs the person to take over its screen before it goes on. The request is in the conversation on their screen.";

    public const string WorkFailed = "The bot could not finish this. What went wrong is in the conversation.";

    public const string WorkStopped = "The bot was stopped before it finished.";

    public const string NoReply = "The bot finished and wrote no reply.";

    public const string RestIsWritten = "The rest of the reply is in the conversation.";

    /// <summary>The most of a reply that is handed to the voice to say. A longer reply is read from the conversation.</summary>
    public const int MaxSpokenReplyCharacters = 4000;

    private const int MaxHistoryCharacters = 4000;

    private const int MaxVoiceLength = 200;

    private const int MaxModelLength = 200;

    private const int MaxRememberedVoices = 500;

    private const int TitleLength = 80;

    private static readonly TimeSpan voiceMemory = TimeSpan.FromMinutes(10);

    private static readonly FrozenSet<string> recordingTypes = FrozenSet.ToFrozenSet(
        ["audio/webm", "audio/wav", "audio/x-wav", "audio/wave", "audio/mp4", "audio/m4a", "audio/x-m4a", "audio/mpeg", "audio/mp3", "audio/ogg"],
        StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<(string Address, string Model), (string Voice, DateTimeOffset At)> firstVoices = new();

    #endregion

    #region Properties

    private DeploymentPolicy Rules => deployment.Value.Rules;

    #endregion

    #region Options And Settings

    /// <summary>What can listen, speak and hold a live call in the space, what each slot uses now, and the server's limits.</summary>
    public async Task<VoiceOptionsView> GetOptions(Guid spaceId, Guid userId, CancellationToken cancellationToken)
    {
        var loaded = await scopes.For(spaceId, userId).Transact(services => Load(services, spaceId, cancellationToken), cancellationToken);
        return View(loaded);
    }

    /// <summary>
    /// Saves the space's choices. A choice names a connection of this space and a model its catalog marks for that
    /// slot; anything else is refused with a sentence and nothing is saved.
    /// </summary>
    public Task<VoiceOutcome<VoiceOptionsView>> SaveSettings(Guid spaceId, Guid userId, VoiceSettings settings, CancellationToken cancellationToken) =>
        scopes.For(spaceId, userId).Transact(
            async services =>
            {
                var loaded = await Load(services, spaceId, cancellationToken);
                var problem = FindProblem(VoiceSlot.Listen, settings.Listen, loaded)
                    ?? FindProblem(VoiceSlot.Speak, settings.Speak, loaded)
                    ?? FindProblem(VoiceSlot.Live, settings.Live, loaded);

                if (problem is not null)
                {
                    return new VoiceOutcome<VoiceOptionsView>(null, new VoiceProblem(VoiceProblemKind.Invalid, problem));
                }

                var space = await services.GetRequiredService<AccountRepository>().GetSpaceForUpdate(spaceId, cancellationToken);

                if (space is null)
                {
                    return new VoiceProblem(VoiceProblemKind.NotFound, "This space no longer exists.");
                }

                var kept = settings is { Listen: null, Speak: null, Live: null } ? null : Normalize(settings);
                space.Settings = space.Settings with { Voice = kept };
                logger.LogInformation("Space {SpaceId} saved its voice settings", spaceId);
                return new VoiceOutcome<VoiceOptionsView>(View(loaded with { Settings = kept }), null);
            },
            cancellationToken);

    /// <summary>
    /// The voices a model offers. A null <paramref name="connectionId"/> asks the server's own speech service. A
    /// model marked for live calls answers with the voices Drover knows; any other asks the provider.
    /// </summary>
    public async Task<VoiceOutcome<IReadOnlyList<VoiceOption>>> ListVoices(
        Guid spaceId,
        Guid userId,
        Guid? connectionId,
        string model,
        CancellationToken cancellationToken)
    {
        if (model is not { Length: > 0 and <= MaxModelLength })
        {
            return new VoiceProblem(VoiceProblemKind.Invalid, "Name the model.");
        }

        var prepared = await scopes.For(spaceId, userId).Transact<(PreparedCall Call, bool IsLive)?>(
            async services =>
            {
                var loaded = await Load(services, spaceId, cancellationToken);
                var live = Find(VoiceSlot.Live, connectionId, model, loaded);
                var candidate = live ?? Find(VoiceSlot.Speak, connectionId, model, loaded);
                return candidate is null ? null : (await Prepare(services, candidate, null, spaceId, cancellationToken), live is not null);
            },
            cancellationToken);

        if (prepared is not { } found)
        {
            return new VoiceProblem(VoiceProblemKind.NotFound, "That model is not marked for speech in this space.");
        }

        if (IsSwitchedOff(found.Call))
        {
            return NoAdapter(found.Call);
        }

        if (found.IsLive)
        {
            return new VoiceOutcome<IReadOnlyList<VoiceOption>>(
                callers.FirstOrDefault(adapter => adapter.Kind == found.Call.Candidate.Kind)?.ListVoices(model) ?? [],
                null);
        }

        if (Endpoint(found.Call, speakers.FirstOrDefault(adapter => adapter.Kind == found.Call.Candidate.Kind)) is not var (adapter, endpoint))
        {
            return NoAdapter(found.Call);
        }

        try
        {
            using var attempt = OutboundAttempt.Begin(found.Call.Policy);
            return new VoiceOutcome<IReadOnlyList<VoiceOption>>(await adapter.ListVoices(endpoint, found.Call.Key, model, cancellationToken), null);
        }
        catch (ModelProviderException failure)
        {
            logger.LogWarning("Listing voices failed through connection {ConnectionId}: {Failure}", connectionId, failure.Failure);
            return new VoiceProblem(VoiceProblemKind.Provider, failure.Message);
        }
    }

    #endregion

    #region Transcription

    /// <summary>
    /// Writes down a recording. The audio goes from the request to the provider through memory only, and no more of
    /// it is read than <c>MaxVoiceRecordingBytes</c>.
    /// </summary>
    public async Task<VoiceOutcome<string>> Transcribe(Guid spaceId, Guid userId, Stream audio, string? mediaType, CancellationToken cancellationToken)
    {
        var type = mediaType?.Split(';')[0].Trim() ?? string.Empty;

        if (!recordingTypes.Contains(type))
        {
            return new VoiceProblem(VoiceProblemKind.Invalid, "Send the recording as WebM, WAV, MP4, MP3 or Ogg audio.");
        }

        var prepared = await PrepareSlot(VoiceSlot.Listen, spaceId, userId, cancellationToken);

        if (prepared is null)
        {
            return new VoiceProblem(VoiceProblemKind.NotReady, "Voice input needs a model that writes down speech. Connect one under Models.");
        }

        if (Endpoint(prepared, listeners.FirstOrDefault(adapter => adapter.Kind == prepared.Candidate.Kind)) is not var (adapter, endpoint))
        {
            return NoAdapter(prepared);
        }

        var bounded = new BoundedAudio(audio, Rules.MaxVoiceRecordingBytes);
        var started = time.GetTimestamp();

        try
        {
            using var attempt = OutboundAttempt.Begin(prepared.Policy);
            var transcript = await adapter.Transcribe(
                endpoint,
                prepared.Key,
                new TranscriptionRequest(bounded, type, prepared.Candidate.Model, Rules.MaxVoiceRecordingBytes),
                cancellationToken);
            logger.LogInformation(
                "Transcribed a recording of {Bytes} bytes for space {SpaceId} through connection {ConnectionId} in {ElapsedMs} ms ({AudioSeconds} s of audio)",
                bounded.BytesRead,
                spaceId,
                prepared.Candidate.ConnectionId,
                (long)time.GetElapsedTime(started).TotalMilliseconds,
                transcript.Seconds);
            return new VoiceOutcome<string>(transcript.Text, null);
        }
        catch (Exception refused) when (bounded.Exceeded)
        {
            logger.LogInformation("A recording was refused as larger than {MaxBytes} bytes ({Reason})", Rules.MaxVoiceRecordingBytes, refused.GetType().Name);
            return new VoiceProblem(VoiceProblemKind.TooLarge, RecordingTooLarge(Rules.MaxVoiceRecordingBytes));
        }
        catch (ModelProviderException failure)
        {
            logger.LogWarning("Transcription failed through connection {ConnectionId}: {Failure}", prepared.Candidate.ConnectionId, failure.Failure);
            return new VoiceProblem(VoiceProblemKind.Provider, failure.Message);
        }
    }

    public static string RecordingTooLarge(long maxBytes) =>
        $"This recording is too long. This server takes recordings up to {maxBytes / (1024 * 1024)} MB.";

    #endregion

    #region Read Aloud

    /// <summary>
    /// Works out one read-aloud answer without asking for it yet: the words (a message's text parts, read as the
    /// person who asked so row-level security decides whether they may, or the sample text), the model, the voice
    /// and the pieces. Exactly one of <paramref name="messageId"/> and <paramref name="text"/> is given.
    /// </summary>
    public async Task<VoiceOutcome<SpeechPlan>> PrepareSpeech(
        Guid spaceId,
        Guid userId,
        Guid? messageId,
        string? text,
        string? voice,
        string? format,
        CancellationToken cancellationToken)
    {
        if (messageId is null == string.IsNullOrWhiteSpace(text))
        {
            return new VoiceProblem(VoiceProblemKind.Invalid, "Name a message to read aloud, or send text, not both.");
        }

        if (format is not null && SpeechFormats.Parse(format) is null)
        {
            return new VoiceProblem(VoiceProblemKind.Invalid, "The format is mp3, wav, opus or pcm.");
        }

        if (voice is { Length: > MaxVoiceLength } || voice?.Any(char.IsControl) == true)
        {
            return new VoiceProblem(VoiceProblemKind.Invalid, "That is not a voice name.");
        }

        var read = await scopes.For(spaceId, userId).Transact(
            async services =>
            {
                var words = messageId is { } id ? await ReadMessageText(services, id, cancellationToken) : text!.Trim();
                var loaded = await Load(services, spaceId, cancellationToken);
                var pick = VoiceChoices.Resolve(VoiceSlot.Speak, loaded.Settings, loaded.Sources, server);
                return (Words: words, Call: pick is null ? null : await Prepare(services, pick.Candidate, pick.Voice, spaceId, cancellationToken));
            },
            cancellationToken);

        if (read.Words is null)
        {
            return new VoiceProblem(VoiceProblemKind.NotFound, "That message is not in a conversation you can read.");
        }

        if (read.Words.Length == 0)
        {
            return new VoiceProblem(VoiceProblemKind.Invalid, "This message has no text to read aloud.");
        }

        if (read.Words.Length > Rules.MaxReadAloudCharacters)
        {
            return new VoiceProblem(
                VoiceProblemKind.TooLarge,
                $"This text is {read.Words.Length} characters. This server reads aloud up to {Rules.MaxReadAloudCharacters}.");
        }

        if (read.Call is not { } call)
        {
            return new VoiceProblem(VoiceProblemKind.NotReady, "Reading aloud needs a speech model. Connect one under Models.");
        }

        if (Endpoint(call, speakers.FirstOrDefault(candidate => candidate.Kind == call.Candidate.Kind)) is not var (adapter, endpoint))
        {
            return NoAdapter(call);
        }

        var spoken = string.IsNullOrWhiteSpace(voice) ? call.Voice : voice.Trim();

        try
        {
            spoken ??= await FirstVoice(adapter, endpoint, call, cancellationToken);
        }
        catch (ModelProviderException failure)
        {
            logger.LogWarning("Listing voices failed through connection {ConnectionId}: {Failure}", call.Candidate.ConnectionId, failure.Failure);
            return new VoiceProblem(VoiceProblemKind.Provider, failure.Message);
        }

        if (spoken is null)
        {
            return new VoiceProblem(VoiceProblemKind.NotReady, "This speech model lists no voices. Choose a voice in the voice settings.");
        }

        var pieces = SpeechText.Split(read.Words, SpeechText.PieceCharactersFor(endpoint.BaseAddress));
        var chosen = ChooseFormat(SpeechFormats.Parse(format) ?? SpeechFormat.Mp3, endpoint.BaseAddress, pieces.Count);
        return new VoiceOutcome<SpeechPlan>(
            new SpeechPlan
            {
                MediaType = chosen.MediaType(),
                Format = chosen,
                Pieces = pieces,
                Model = call.Candidate.Model,
                Voice = spoken,
                ConnectionId = call.Candidate.ConnectionId,
                Adapter = adapter,
                Endpoint = endpoint,
                Key = call.Key,
                Policy = call.Policy,
            },
            null);
    }

    /// <summary>
    /// The format the provider is asked for. A provider that takes short pieces answers only in WAV, so it is always
    /// asked for WAV and its answers are joined. Anywhere else, several pieces are asked for as MP3, whose pieces
    /// play one after another when simply put end to end; other containers do not.
    /// </summary>
    public static SpeechFormat ChooseFormat(SpeechFormat asked, Uri address, int pieces) =>
        SpeechText.TakesShortPieces(address)
            ? SpeechFormat.Wav
            : pieces > 1 ? SpeechFormat.Mp3 : asked;

    /// <summary>
    /// Asks the provider for each piece in order and hands the audio to <paramref name="write"/> as it arrives. Null
    /// when all of it was written. A failure before the first chunk has written nothing, so the caller can still
    /// answer with the problem; after it, the caller can only cut the answer short.
    /// </summary>
    public async Task<VoiceProblem?> Speak(
        SpeechPlan plan,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> write,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var join = plan is { Format: SpeechFormat.Wav, Pieces.Count: > 1 } ? new WavJoin() : null;
        long bytes = 0;

        try
        {
            // One flow from here to the last chunk, so every request the adapter makes carries the space's rule.
            using var attempt = OutboundAttempt.Begin(plan.Policy);

            foreach (var piece in plan.Pieces)
            {
                join?.StartPiece();

                await foreach (var chunk in plan.Adapter.Speak(plan.Endpoint, plan.Key, new SpeechRequest(piece, plan.Model, plan.Voice, plan.Format), cancellationToken))
                {
                    var audio = join is null ? chunk : join.Take(chunk);

                    if (audio.Length > 0)
                    {
                        bytes += audio.Length;
                        await write(audio, cancellationToken);
                    }
                }
            }

            logger.LogInformation(
                "Read {Characters} characters aloud in {Pieces} pieces through connection {ConnectionId}: {Bytes} bytes in {ElapsedMs} ms",
                plan.Pieces.Sum(piece => piece.Length),
                plan.Pieces.Count,
                plan.ConnectionId,
                bytes,
                (long)time.GetElapsedTime(started).TotalMilliseconds);
            return null;
        }
        catch (ModelProviderException failure)
        {
            logger.LogWarning("Reading aloud failed through connection {ConnectionId}: {Failure}", plan.ConnectionId, failure.Failure);
            return new VoiceProblem(VoiceProblemKind.Provider, failure.Message);
        }
    }

    #endregion

    #region Live Calls

    /// <summary>
    /// Everything a live call in a conversation needs, read in one short transaction as the caller: whether the
    /// conversation can take a call now, the bot's instructions, the last messages as context, the live model, its
    /// key and the space's rule. Nothing is opened yet.
    /// </summary>
    public Task<VoiceOutcome<CallPlan>> PrepareCall(Guid spaceId, Guid userId, Guid threadId, CancellationToken cancellationToken) =>
        scopes.For(spaceId, userId).Transact(
            async services =>
            {
                var thread = await services.GetRequiredService<ThreadRepository>().FindThread(threadId, cancellationToken);

                if (thread is null)
                {
                    return new VoiceProblem(VoiceProblemKind.NotFound, "That conversation is not one you can read.");
                }

                if (thread.Origin is ThreadOrigin.Delegation)
                {
                    return new VoiceProblem(VoiceProblemKind.NotReady, RunService.HandoffIsReadOnly);
                }

                var bot = await services.GetRequiredService<BotRepository>().FindBot(thread.BotId, cancellationToken);

                if (bot is not { Status: BotStatus.Active })
                {
                    return new VoiceProblem(VoiceProblemKind.NotReady, "This bot is paused or removed, so it cannot answer.");
                }

                if (await services.GetRequiredService<RunRepository>().FindActiveRun(threadId, cancellationToken) is not null)
                {
                    return new VoiceProblem(VoiceProblemKind.NotReady, BotIsWorking);
                }

                var loaded = await Load(services, spaceId, cancellationToken);
                var pick = VoiceChoices.Resolve(VoiceSlot.Live, loaded.Settings, loaded.Sources, server);
                var call = pick is null ? null : await Prepare(services, pick.Candidate, pick.Voice, spaceId, cancellationToken);

                if (call is null)
                {
                    return new VoiceProblem(VoiceProblemKind.NotReady, "A live call needs a model that holds live calls. Connect one under Models.");
                }

                if (Endpoint(call, callers.FirstOrDefault(candidate => candidate.Kind == call.Candidate.Kind)) is not var (adapter, endpoint))
                {
                    return NoAdapter(call);
                }

                var voice = call.Voice ?? (adapter.ListVoices(call.Candidate.Model) is [var first, ..] ? first.Id : null);
                var handsOver = adapter.HandsWorkOver(call.Candidate.Model);

                if (voice is null)
                {
                    return new VoiceProblem(VoiceProblemKind.NotReady, "This live model lists no voices. Choose a voice in the voice settings.");
                }

                // The person's words are written down by the space's listening model when it lives on the same
                // connection as the live model. Without one the provider decides, and some then send no words.
                var listener = VoiceChoices.Resolve(VoiceSlot.Listen, loaded.Settings, loaded.Sources, server);
                var transcriber = listener is { Candidate.ConnectionId: { } listening } && listening == call.Candidate.ConnectionId
                    ? listener.Candidate.Model
                    : null;
                var history = await services.GetRequiredService<MessageRepository>()
                    .ListLatestMessages(threadId, CallHistoryMessages, cancellationToken, conversationOnly: true);
                return new VoiceOutcome<CallPlan>(
                    new CallPlan
                    {
                        SpaceId = spaceId,
                        UserId = userId,
                        ThreadId = threadId,
                        BotId = bot.Id,
                        Model = call.Candidate.Model,
                        Voice = voice,
                        MaxMinutes = Rules.MaxCallMinutes == int.MaxValue ? null : Rules.MaxCallMinutes,
                        ConnectionId = call.Candidate.ConnectionId,
                        Adapter = adapter,
                        Endpoint = endpoint,
                        Key = call.Key,
                        Policy = call.Policy,
                        HandsWorkOver = handsOver,
                        Options = new RealtimeVoiceOptions(call.Candidate.Model, voice, CallInstructions(bot, handsOver))
                        {
                            History = [.. history.Select(ToTurn).OfType<RealtimeTurn>()],
                            TranscriptionModel = transcriber,
                        },
                    },
                    null);
            },
            cancellationToken);

    /// <summary>Connects to the live model. No transaction is open; the connection carries the space's outbound rule.</summary>
    public async Task<VoiceOutcome<IRealtimeVoiceSession>> OpenCall(CallPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            using var attempt = OutboundAttempt.Begin(plan.Policy);
            var session = await plan.Adapter.Open(plan.Endpoint, plan.Key, plan.Options, cancellationToken);
            logger.LogInformation("Live call opened in thread {ThreadId} through connection {ConnectionId}", plan.ThreadId, plan.ConnectionId);
            return new VoiceOutcome<IRealtimeVoiceSession>(session, null);
        }
        catch (ModelProviderException failure)
        {
            logger.LogWarning("A live call could not be opened through connection {ConnectionId}: {Failure}", plan.ConnectionId, failure.Failure);
            return new VoiceProblem(VoiceProblemKind.Provider, failure.Message);
        }
    }

    /// <summary>
    /// Adds what was just said on a call to its conversation, in one short transaction: the person's words as their
    /// message and the bot's as its reply, either of which may be missing. A reply written on a call belongs to no
    /// run. If a run has begun on the conversation meanwhile, nothing is written, so the conversation never holds
    /// two writers' messages out of order.
    /// </summary>
    public async Task<CallWrite> RecordExchange(CallPlan plan, string? personText, string? botText, CancellationToken cancellationToken)
    {
        List<Message> said =
        [
            .. string.IsNullOrWhiteSpace(personText)
                ? Array.Empty<Message>()
                : [new Message { Role = MessageRole.User, AuthorUserId = plan.UserId, Parts = [new TextPart(Clip(personText))], CreatedAt = time.GetUtcNow() }],
            .. string.IsNullOrWhiteSpace(botText)
                ? Array.Empty<Message>()
                : [new Message { Role = MessageRole.Assistant, AuthorBotId = plan.BotId, Model = plan.Model, Parts = [new TextPart(Clip(botText))], CreatedAt = time.GetUtcNow() }],
        ];

        if (said.Count == 0)
        {
            return CallWrite.Written;
        }

        Guid? owner = null;
        var written = await scopes.For(plan.SpaceId, plan.UserId).Transact(
            async services =>
            {
                var thread = await services.GetRequiredService<ThreadRepository>().GetThreadForUpdate(plan.ThreadId, cancellationToken);

                if (thread is null)
                {
                    return CallWrite.Gone;
                }

                if (await services.GetRequiredService<RunRepository>().FindActiveRun(plan.ThreadId, cancellationToken) is not null)
                {
                    return CallWrite.RunActive;
                }

                await services.GetRequiredService<MessageRepository>().AppendMessages(thread, said, cancellationToken);
                thread.LastActivityAt = time.GetUtcNow();

                if (!thread.IsMain && !string.IsNullOrWhiteSpace(personText))
                {
                    thread.Title ??= ThreadService.Shorten(personText.Trim()[..Math.Min(personText.Trim().Length, TitleLength)]);
                }

                owner = thread.OwnerUserId;
                return CallWrite.Written;
            },
            cancellationToken);

        if (written is CallWrite.Written)
        {
            foreach (var person in new[] { plan.UserId, owner ?? plan.UserId }.Distinct())
            {
                signals.Publish(plan.SpaceId, person, new UserSignal(UserSignalKinds.ThreadChanged, plan.ThreadId, null));
            }

            logger.LogInformation("A live call added {Messages} messages to thread {ThreadId}", said.Count, plan.ThreadId);
        }

        return written;
    }

    #endregion

    #region Handed-Over Work

    /// <summary>
    /// Does what the voice of a call handed over: <paramref name="asked"/> becomes the person's message in the
    /// conversation, exactly as if they had sent it, and the bot answers it in a run like any other, with its own
    /// model, rules, tools and limits. Each item is something for the voice to tell the person: that the bot waits
    /// for them, and at the end what the bot replied or why there is no reply. No transaction is open while the run
    /// is waited for. Stopping the wait does not stop the run.
    /// </summary>
    public async IAsyncEnumerable<string> DoWork(CallPlan plan, string? asked, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(asked))
        {
            yield return NothingAsked;
            yield break;
        }

        MessagePost? post = null;
        var tenant = scopes.For(plan.SpaceId, plan.UserId);
        await tenant.InScope(async services =>
            post = await services.GetRequiredService<RunService>().PostMessage(plan.ThreadId, Clip(asked), [], steer: true, plan.UserId, cancellationToken));

        if (post is not { Problem: PostProblem.None, Run: { } run })
        {
            logger.LogWarning("A live call could not hand its work to the bot in thread {ThreadId}: {Problem}", plan.ThreadId, post?.Problem);
            yield return post?.Detail ?? WorkNotSent;
            yield break;
        }

        logger.LogInformation("A live call handed work to run {RunId} in thread {ThreadId}", run.Id, plan.ThreadId);

        // A message sent while the bot works can be answered by a run that follows the one it joined.
        for (var watched = run; watched is not null;)
        {
            run = watched;

            await foreach (var happened in feed.Watch(plan.SpaceId, plan.UserId, run.RootRunId, run.LastEventSeq, cancellationToken))
            {
                if (happened is { Type: RunEventTypes.RunWaiting } && happened.RunId == run.RootRunId && Waiting(happened) is { } why)
                {
                    yield return why;
                }
            }

            watched = await tenant.Transact(
                services => services.GetRequiredService<RunRepository>().FindActiveRun(plan.ThreadId, cancellationToken),
                cancellationToken);
        }

        yield return await tenant.Transact(services => ReadOutcome(services, plan.ThreadId, run.Id, cancellationToken), cancellationToken);
    }

    private static string? Waiting(RunEventResponse waiting) =>
        RunEventTypes.Read<RunStatusChanged>(waiting).Reason switch
        {
            RunReasons.AwaitingApproval => WaitingForApproval,
            RunReasons.AwaitingTakeover => WaitingForTakeover,
            _ => null,
        };

    /// <summary>What the run came to, for the voice to say: the reply's text made plain for speech, or one sentence.</summary>
    private static async Task<string> ReadOutcome(IServiceProvider services, Guid threadId, Guid runId, CancellationToken cancellationToken)
    {
        var ended = await services.GetRequiredService<RunRepository>().FindRun(runId, cancellationToken);

        if (ended is not { Status: RunStatus.Completed })
        {
            return ended is { Status: RunStatus.Cancelled } ? WorkStopped : WorkFailed;
        }

        var reply = await services.GetRequiredService<MessageRepository>().FindLatestReply(threadId, cancellationToken);
        var text = reply is not null && reply.RunId == runId ? SpeechText.Plain(TextOf(reply)).Trim() : string.Empty;

        return text.Length switch
        {
            0 => NoReply,
            > MaxSpokenReplyCharacters => $"{text[..MaxSpokenReplyCharacters]}\n\n{RestIsWritten}",
            _ => text,
        };
    }

    #endregion

    #region Reading The Space

    /// <summary>The space's connections with their offered models, and its voice settings. Runs in the caller's transaction.</summary>
    private static async Task<Loaded> Load(IServiceProvider services, Guid spaceId, CancellationToken cancellationToken)
    {
        var repository = services.GetRequiredService<ModelConnectionRepository>();
        var connections = await repository.ListConnections(cancellationToken);
        var catalog = (await repository.ListAvailableCatalog(cancellationToken)).ToLookup(entry => entry.ModelConnectionId);
        var space = await services.GetRequiredService<AccountRepository>().GetSpace(spaceId, cancellationToken);
        return new Loaded([.. connections.Select(connection => new VoiceSource(connection, [.. catalog[connection.Id]]))], space?.Settings.Voice);
    }

    private VoiceOptionsView View(Loaded loaded) =>
        new(
            Slot(VoiceSlot.Listen, loaded),
            Slot(VoiceSlot.Speak, loaded),
            Slot(VoiceSlot.Live, loaded),
            Rules.MaxVoiceRecordingBytes,
            Rules.MaxReadAloudCharacters,
            Rules.MaxCallMinutes);

    private VoiceSlotView Slot(VoiceSlot slot, Loaded loaded) =>
        new(VoiceChoices.Candidates(slot, loaded.Sources, server), VoiceChoices.Resolve(slot, loaded.Settings, loaded.Sources, server));

    private VoiceCandidate? Find(VoiceSlot slot, Guid? connectionId, string model, Loaded loaded) =>
        VoiceChoices.Candidates(slot, loaded.Sources, server)
            .FirstOrDefault(candidate => candidate.ConnectionId == connectionId && candidate.Model == model);

    private async Task<PreparedCall?> PrepareSlot(VoiceSlot slot, Guid spaceId, Guid userId, CancellationToken cancellationToken) =>
        await scopes.For(spaceId, userId).Transact(
            async services =>
            {
                var loaded = await Load(services, spaceId, cancellationToken);
                var pick = VoiceChoices.Resolve(slot, loaded.Settings, loaded.Sources, server);
                return pick is null ? null : await Prepare(services, pick.Candidate, pick.Voice, spaceId, cancellationToken);
            },
            cancellationToken);

    /// <summary>Reveals the key of the candidate's connection and works out the space's outbound rule. Runs in the caller's transaction.</summary>
    private static async Task<PreparedCall> Prepare(
        IServiceProvider services,
        VoiceCandidate candidate,
        string? voice,
        Guid spaceId,
        CancellationToken cancellationToken)
    {
        var connection = candidate.ConnectionId is { } id
            ? await services.GetRequiredService<ModelConnectionRepository>().FindConnection(id, cancellationToken)
            : null;
        var credentials = services.GetRequiredService<ModelCredentials>();

        // Speech adapters serve key connections only: a person's sign-in is refused without being read.
        var credential = connection switch
        {
            null => ModelCredential.None,
            _ when credentials.IsSignInKind(connection.Kind) =>
                ModelCredential.Refused(ModelCredentialProblem.NotAvailable, "Speech is not available through a sign-in connection."),
            _ => await credentials.Read(services, connection, cancellationToken),
        };
        var policy = await services.GetRequiredService<PolicyService>().GetEffective(spaceId, cancellationToken);
        return new PreparedCall(candidate, voice, credential, policy);
    }

    /// <summary>The adapter with the address it is called at, or null when this server has no adapter or address for the candidate.</summary>
    private (TAdapter Adapter, ModelEndpoint Endpoint)? Endpoint<TAdapter>(PreparedCall call, TAdapter? adapter)
        where TAdapter : class
    {
        var fixedAddress = adapter switch
        {
            ISpeechToText listener => listener.FixedAddress,
            ITextToSpeech speaker => speaker.FixedAddress,
            IRealtimeVoice caller => caller.FixedAddress,
            _ => null,
        };

        // Speech adapters serve key connections only. A credential that could not be had, or a person's sign-in,
        // which only a run renews, is refused here, before anything is sent.
        if (call.Credential is { Problem: not null } or { SignIn: not null })
        {
            return null;
        }

        return adapter is not null && !IsSwitchedOff(call) && (fixedAddress ?? call.Candidate.Address) is { } address
            ? (adapter, new ModelEndpoint(call.Candidate.Kind, address) { Limits = new DiscoveryLimits(Rules.MaxModelListBytes, Rules.MaxModelsPerConnection) })
            : null;
    }

    /// <summary>Whether the candidate is a stored connection of a kind the operator switched off.</summary>
    private bool IsSwitchedOff(PreparedCall call) =>
        call.Candidate.ConnectionId is not null && !Rules.OffersModelProvider(call.Candidate.Kind);

    private VoiceProblem NoAdapter(PreparedCall call) =>
        new(
            VoiceProblemKind.NotReady,
            IsSwitchedOff(call)
                ? ModelProviderCatalog.SwitchedOff(call.Candidate.Kind)
                : "This server has no speech adapter for that connection.");

    #endregion

    #region Settings Validation

    private string? FindProblem(VoiceSlot slot, VoiceChoice? choice, Loaded loaded)
    {
        if (choice is null)
        {
            return null;
        }

        if (choice.Model is not { Length: > 0 and <= MaxModelLength })
        {
            return "Name the model.";
        }

        if (choice.Voice is { Length: > MaxVoiceLength } || choice.Voice?.Any(char.IsControl) == true)
        {
            return "That is not a voice name.";
        }

        if (loaded.Sources.All(source => source.Connection.Id != choice.ConnectionId))
        {
            return "That connection is not in this space.";
        }

        return Find(slot, choice.ConnectionId, choice.Model, loaded) is null
            ? slot switch
            {
                VoiceSlot.Listen => "That model is not marked as writing down speech. Mark it under Models first.",
                VoiceSlot.Speak => "That model is not marked as reading aloud. Mark it under Models first.",
                _ => "That model is not marked as holding live calls. Mark it under Models first.",
            }
            : null;
    }

    private static VoiceSettings Normalize(VoiceSettings settings) =>
        new() { Listen = Normalize(settings.Listen), Speak = Normalize(settings.Speak), Live = Normalize(settings.Live) };

    private static VoiceChoice? Normalize(VoiceChoice? choice) =>
        choice is null ? null : choice with { Voice = string.IsNullOrWhiteSpace(choice.Voice) ? null : choice.Voice.Trim() };

    #endregion

    #region Words

    /// <summary>
    /// The text parts of a message, as the caller may read it: null when row-level security shows them no such
    /// message. Reasoning, tool calls, tool results and files are never read aloud.
    /// </summary>
    private static async Task<string?> ReadMessageText(IServiceProvider services, Guid messageId, CancellationToken cancellationToken) =>
        await services.GetRequiredService<MessageRepository>().FindMessage(messageId, cancellationToken) is { } message
            ? SpeechText.Plain(TextOf(message))
            : null;

    private static string TextOf(Message message) =>
        string.Join("\n\n", message.Parts.OfType<TextPart>().Select(part => part.Text.Trim()).Where(text => text.Length > 0));

    private static RealtimeTurn? ToTurn(Message message)
    {
        var text = message is { Withheld: false, Role: MessageRole.User or MessageRole.Assistant } ? TextOf(message) : string.Empty;
        return text.Length == 0 ? null : new RealtimeTurn(message.Role is MessageRole.Assistant, Clip(text, MaxHistoryCharacters));
    }

    private static string CallInstructions(Bot bot, bool handsOver)
    {
        var name = bot.Name.Trim();
        var identity = $"""
            You are {name}, the bot this person called. Speak in the first person as {name}.
            Use {name} when asked who you are. The speech model and its provider are separate from your identity.
            """;
        var description = string.IsNullOrWhiteSpace(bot.DescriptionForBots)
            ? string.Empty
            : $"\n\nBot description (use this to explain your role):\n{bot.DescriptionForBots.Trim()}";
        var instructions = handsOver || string.IsNullOrWhiteSpace(bot.Instructions)
            ? string.Empty
            : $"\n\nBot instructions:\n{bot.Instructions.Trim()}";

        return $"{identity}{description}{instructions}\n\n{SpokenReplyInstruction}";
    }

    private static string Clip(string text, int length = RunService.MaxMessageLength)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= length ? trimmed : trimmed[..length];
    }

    /// <summary>The first voice the provider lists for the model, remembered for a while so not every answer asks again.</summary>
    private async Task<string?> FirstVoice(ITextToSpeech adapter, ModelEndpoint endpoint, PreparedCall call, CancellationToken cancellationToken)
    {
        var key = (endpoint.BaseAddress.AbsoluteUri, call.Candidate.Model);
        var now = time.GetUtcNow();

        if (firstVoices.TryGetValue(key, out var remembered) && now - remembered.At < voiceMemory)
        {
            return remembered.Voice;
        }

        IReadOnlyList<VoiceOption> voices;

        using (OutboundAttempt.Begin(call.Policy))
        {
            voices = await adapter.ListVoices(endpoint, call.Key, call.Candidate.Model, cancellationToken);
        }

        if (voices is not [var first, ..])
        {
            return null;
        }

        if (firstVoices.Count >= MaxRememberedVoices)
        {
            firstVoices.Clear();
        }

        firstVoices[key] = (first.Id, now);
        return first.Id;
    }

    #endregion

    #region Nested Types

    private sealed record Loaded(IReadOnlyList<VoiceSource> Sources, VoiceSettings? Settings);

    /// <summary>A class, not a record: it holds a key, which a record would print.</summary>
    private sealed class PreparedCall(VoiceCandidate candidate, string? voice, ModelCredential credential, EffectivePolicy policy)
    {
        #region Properties

        public VoiceCandidate Candidate => candidate;

        public string? Voice => voice;

        /// <summary>What the call sends, from <see cref="ModelCredentials"/>.</summary>
        public ModelCredential Credential => credential;

        public string? Key => credential.Value;

        public EffectivePolicy Policy => policy;

        #endregion
    }

    /// <summary>
    /// The request's audio, counted as it is read. Reading past the limit fails and leaves <see cref="Exceeded"/>
    /// set, so the caller can tell that failure from any other however the adapter reported it.
    /// </summary>
    private sealed class BoundedAudio(Stream inner, long maxBytes) : Stream
    {
        #region Properties

        public long BytesRead { get; private set; }

        public bool Exceeded { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        #endregion

        #region Public Methods

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;

            if (BytesRead > maxBytes)
            {
                Exceeded = true;
                throw new IOException("The recording is larger than this server accepts.");
            }

            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("The recording is read asynchronously.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    #endregion
}
