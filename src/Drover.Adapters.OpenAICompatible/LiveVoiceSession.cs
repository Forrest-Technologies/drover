namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// One open call with a model of the live protocol. The model listens and speaks at the same time and decides for
/// itself when to do either, so there are no turns on the wire: its voice and both speakers' words arrive as pieces
/// with no end marked. This joins the words into whole utterances, one ending when its speaker has been quiet for a
/// moment. The model answers nothing of substance: it hands the work over, and what the work came to is reported
/// back for it to say.
/// </summary>
internal sealed class LiveVoiceSession(VoiceSocket socket, TimeProvider time) : IRealtimeVoiceSession
{
    #region Private Fields

    /// <summary>How long a speaker is quiet before what they said counts as whole.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// The same, once work was handed over. The notice can arrive before the last of the words that asked for it, so
    /// the words are still waited for, but not for as long.
    /// </summary>
    public static readonly TimeSpan QuietBeforeWork = TimeSpan.FromMilliseconds(700);

    /// <summary>The provider takes 500 tokens in one report. A piece is kept under that by a count that errs high.</summary>
    public const int MaxReportTokens = 450;

    private const int MaxUtteranceCharacters = 200_000;

    private static readonly TimeSpan check = TimeSpan.FromMilliseconds(100);

    private readonly Utterance person = new();

    private readonly Utterance bot = new();

    // Work handed over whose words are still being waited for.
    private readonly List<string> handedOver = [];

    private long reports;

    #endregion

    #region Interface Implementations

    public ValueTask SendAudio(ReadOnlyMemory<byte> samples, CancellationToken cancellationToken) =>
        samples.IsEmpty
            ? ValueTask.CompletedTask
            : socket.Send(new { type = "session.input_audio.append", audio = Convert.ToBase64String(samples.Span) }, cancellationToken);

    /// <summary>The protocol has no way to cut an answer off or to say how much of it was heard: the model yields by itself.</summary>
    public ValueTask Interrupt(int heardMilliseconds, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public async ValueTask Report(string workId, string text, CancellationToken cancellationToken)
    {
        foreach (var piece in LiveText.Pieces(text, MaxReportTokens))
        {
            await socket.Send(
                new
                {
                    type = "session.commentary.append",
                    event_id = $"drover_report_{Interlocked.Increment(ref reports)}",
                    delegation_id = workId,
                    content = piece,
                },
                cancellationToken);
        }
    }

    public async IAsyncEnumerable<RealtimeVoiceEvent> Read([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Task<JsonDocument?>? receiving = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            receiving ??= socket.Receive(cancellationToken);

            // While words wait to become whole, the clock is looked at between messages as well as after them.
            if (WordsWait && await Task.WhenAny(receiving, Task.Delay(check, time, cancellationToken)) != receiving)
            {
                foreach (var whole in CloseTheQuiet(everything: false))
                {
                    yield return whole;
                }

                continue;
            }

            var message = await receiving;
            receiving = null;

            if (message is null)
            {
                break;
            }

            using (message)
            {
                var ended = false;

                foreach (var read in Translate(message.RootElement))
                {
                    ended |= read is null;

                    if (read is not null)
                    {
                        yield return read;
                    }
                }

                if (ended)
                {
                    break;
                }
            }

            foreach (var whole in CloseTheQuiet(everything: false))
            {
                yield return whole;
            }
        }

        foreach (var whole in CloseTheQuiet(everything: true))
        {
            yield return whole;
        }
    }

    public ValueTask DisposeAsync()
    {
        socket.Dispose();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Session Setup

    /// <summary>
    /// Starts the session: the model, the voice, what the model is told, the audio format, and the earlier
    /// conversation. None of them can change afterwards. Returns once the provider says the session has started; a
    /// refusal surfaces as <see cref="ModelProviderException"/>.
    /// </summary>
    public async Task Start(RealtimeVoiceOptions options, CancellationToken cancellationToken)
    {
        await socket.Send(
            new
            {
                type = "session.start",
                session = new
                {
                    model = options.Model,
                    instructions = LiveText.Instructions(options),
                    input = LiveText.History(options.History),
                    audio = new
                    {
                        format = new { type = RealtimeAudio.MediaType, rate = RealtimeAudio.SampleRate },
                        output = new { voice = options.Voice },
                    },
                    delegation = new { type = "client" },
                },
            },
            cancellationToken);

        while (await socket.Receive(cancellationToken) is { } message)
        {
            using (message)
            {
                switch (VoiceSocket.ReadString(message.RootElement, "type"))
                {
                    case "session.started":
                        return;

                    case "error":
                        var refused = VoiceSocket.DescribeError(message.RootElement);
                        throw new ModelProviderException(refused.Failure, refused.Message);
                }
            }
        }

        throw ModelProviderException.FromNetwork();
    }

    #endregion

    #region Reading

    private bool WordsWait => person.Waits || bot.Waits || handedOver.Count > 0;

    /// <summary>One provider event as Drover's events. A null says the provider closed the session.</summary>
    private IEnumerable<RealtimeVoiceEvent?> Translate(JsonElement root)
    {
        switch (VoiceSocket.ReadString(root, "type"))
        {
            case "session.output_audio.delta":
                if (VoiceSocket.ReadString(root, "delta") is { Length: > 0 } audio && VoiceSocket.TryDecode(audio, out var samples))
                {
                    yield return new RealtimeAudioOut(samples);
                }

                break;

            case "session.output_transcript.delta":
                if (VoiceSocket.ReadString(root, "delta") is { Length: > 0 } saying)
                {
                    bot.Add(saying, time.GetTimestamp());
                    yield return new RealtimeBotSaying(saying);
                }

                break;

            case "session.input_transcript.delta":
                if (VoiceSocket.ReadString(root, "delta") is { Length: > 0 } heard)
                {
                    person.Add(heard, time.GetTimestamp());
                }

                break;

            case "session.delegation.created":
                if (ReadWork(root) is { } work)
                {
                    handedOver.Add(work);

                    // The words that asked for it are waited for from now, whether or not any have arrived.
                    person.Touch(time.GetTimestamp());
                }

                break;

            case "session.closed":
                yield return null;
                break;

            case "error":
                yield return VoiceSocket.DescribeError(root);
                break;
        }
    }

    /// <summary>Work handed to the application. Work the provider runs for itself is not this session's to answer.</summary>
    private static string? ReadWork(JsonElement root) =>
        root.TryGetProperty("delegation", out var delegation) && delegation.ValueKind == JsonValueKind.Object
        && VoiceSocket.ReadString(delegation, "target") == "client"
            ? VoiceSocket.ReadString(delegation, "id")
            : null;

    /// <summary>
    /// What has become whole: the person's words once they have been quiet, then the work that was waiting for those
    /// words, then the bot's once it has been quiet.
    /// </summary>
    private IEnumerable<RealtimeVoiceEvent> CloseTheQuiet(bool everything)
    {
        var now = time.GetTimestamp();

        if (everything || person.QuietFor(handedOver.Count > 0 ? QuietBeforeWork : Quiet, now, time))
        {
            if (person.Take() is { } said)
            {
                yield return new RealtimePersonSaid(said);
            }

            foreach (var work in handedOver)
            {
                yield return new RealtimeWorkHandedOver(work);
            }

            handedOver.Clear();
        }

        if ((everything || bot.QuietFor(Quiet, now, time)) && bot.Take() is { } answered)
        {
            yield return new RealtimeBotSaid(answered);
        }
    }

    #endregion

    #region Nested Types

    /// <summary>What one speaker is saying, piece by piece, and when its last piece arrived.</summary>
    private sealed class Utterance
    {
        #region Private Fields

        private readonly StringBuilder words = new();

        private long lastAt;

        private bool touched;

        #endregion

        #region Properties

        public bool Waits => touched;

        #endregion

        #region Public Methods

        public void Add(string piece, long now)
        {
            if (words.Length + piece.Length <= MaxUtteranceCharacters)
            {
                words.Append(piece);
            }

            Touch(now);
        }

        public void Touch(long now) => (lastAt, touched) = (now, true);

        public bool QuietFor(TimeSpan quiet, long now, TimeProvider time) => touched && time.GetElapsedTime(lastAt, now) >= quiet;

        /// <summary>The whole of what was said, once, or null when it was nothing.</summary>
        public string? Take()
        {
            var said = words.ToString().Trim();
            words.Clear();
            touched = false;
            return said.Length > 0 ? said : null;
        }

        #endregion
    }

    #endregion
}
