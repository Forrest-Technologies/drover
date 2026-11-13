namespace Drover.Server.Tests;

/// <summary>
/// The event stream of a run, over real HTTP against the in-process server: resume from a cursor, across a server
/// restart, after the run has finished, and who may open it.
/// </summary>
public sealed class RunStreamTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ScriptedModel script = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Drover:SignInRateLimit:PermitLimit"] = "1000",
        ["Runs:HeartbeatSeconds"] = "0.2",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));

        // A read-only tool that waits until the test lets it go, so a test can hold a run in the middle.
        services.AddSingleton(new BuiltinTool(
            "test.gate",
            "Gate",
            "Waits until the test opens it.",
            JsonElement.Parse("""{"type":"object","properties":{}}"""),
            ToolSensitivity.Read,
            async (_, cancellationToken) =>
            {
                await gate.Task.WaitAsync(cancellationToken);
                return JsonElement.Parse("""{"open":true}""");
            }));
    }

    #endregion

    #region Resume

    [Fact]
    public async Task Disconnecting_at_event_N_and_reconnecting_with_the_cursor_delivers_N_plus_one_onward_once_each_in_order()
    {
        script = new ScriptedModel([new TextOutput("One. "), new ToolCallOutput("c1", "test_gate")], [new TextOutput("Two.")]);
        var (client, spaceId, runId) = await StartRun("Go");
        List<RunEventResponse> first = [];

        using (var connection = await client.Open(Events(spaceId, runId)))
        {
            connection.StatusCode.Should().Be(HttpStatusCode.OK);
            connection.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

            await foreach (var frame in Frames(connection))
            {
                frame.Id.Should().Be(frame.Event.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture), "the frame id is the sequence number");
                frame.Type.Should().Be(frame.Event.Type);
                first.Add(frame.Event);

                if (frame.Event.Type == RunEventTypes.ToolStarted)
                {
                    break;
                }
            }
        }

        // The client is gone while the run finishes.
        gate.SetResult();
        await WaitForRun(client, spaceId, runId, "completed");
        var cursor = first[^1].Seq;
        var second = await ReadToEnd(client, Events(spaceId, runId), lastEventId: cursor);
        var byQuery = await ReadToEnd(client, $"{Events(spaceId, runId)}?after={cursor}");
        var whole = await ReadToEnd(client, Events(spaceId, runId));

        first.Select(stored => stored.Seq).Should().Equal(Enumerable.Range(1, first.Count).Select(number => (long)number));
        second.Should().NotBeEmpty();
        second.Select(stored => stored.Seq).Should().Equal(
            Enumerable.Range((int)cursor + 1, second.Count).Select(number => (long)number),
            "events after the cursor arrive once each, in order");
        second[^1].Type.Should().Be(RunEventTypes.RunCompleted);
        byQuery.Should().Equal(second, "the query cursor and the header mean the same");
        whole.Should().Equal([.. first, .. second], "what was seen live and what is replayed are the same events");
    }

    [Fact]
    public async Task A_client_that_connects_after_the_run_finished_receives_the_full_log_and_the_stream_closes()
    {
        script = new ScriptedModel([new TextOutput("All of it.")]);
        var (client, spaceId, runId) = await StartRun("Go");
        var run = await WaitForRun(client, spaceId, runId, "completed");

        var whole = await ReadToEnd(client, Events(spaceId, runId)).WaitAsync(patience, Token);
        var nothingLeft = await ReadToEnd(client, Events(spaceId, runId), lastEventId: run.LastEventSeq).WaitAsync(patience, Token);

        whole.Select(stored => stored.Seq).Should().Equal(Enumerable.Range(1, (int)run.LastEventSeq).Select(number => (long)number));
        whole[0].Type.Should().Be(RunEventTypes.RunQueued);
        whole[^1].Type.Should().Be(RunEventTypes.RunCompleted);
        nothingLeft.Should().BeEmpty("a client that had seen everything gets a stream that closes at once");
    }

    [Fact]
    public async Task The_typed_client_follows_a_run_across_a_server_restart_and_sees_every_event_once_in_order()
    {
        script = new ScriptedModel([new TextOutput("Partial "), new PauseOutput()], [new TextOutput("Whole.")]);
        var (client, spaceId, runId) = await StartRun("Go");
        var api = new DroverApi(NewRelay(client));
        List<RunEventResponse> seen = [];
        var drops = 0;
        var view = RunView.Start(runId);

        var watching = Task.Run(async () =>
        {
            await foreach (var stored in api.WatchRun(spaceId, runId, dropped: _ => Interlocked.Increment(ref drops), cancellationToken: Token))
            {
                lock (seen)
                {
                    seen.Add(stored);
                }

                view = RunReducer.Apply(view, stored);
            }
        }, Token);
        await Until(() => Snapshot(seen).Any(stored => stored.Type == RunEventTypes.MessageDelta));

        await RestartServer();
        await watching.WaitAsync(patience, Token);

        var afterRestart = NewClient();
        (await afterRestart.SignIn(Person.Email, Person.Password)).EnsureSuccessStatusCode();
        var stored = await ReadToEnd(afterRestart, Events(spaceId, runId));
        seen.Select(found => found.Seq).Should().Equal(
            Enumerable.Range(1, seen.Count).Select(number => (long)number),
            "nothing is missed and nothing arrives twice across the restart");
        seen.Should().Equal(stored, "the client saw exactly the run's log");
        seen.Select(found => found.Type).Should().Contain(RunEventTypes.StepDiscarded).And.EndWith(RunEventTypes.RunCompleted);
        view.Status.Should().Be("completed");
        view.Messages.Should().ContainSingle().Which.Parts.Should().ContainSingle().Which.Text.Should().Be("Whole.");
        drops.Should().BeGreaterThan(0, "the client noticed the connection drop and connected again");
    }

    #endregion

    #region Who May Watch

    [Fact]
    public async Task Another_member_cannot_open_the_stream_of_a_private_thread_and_a_person_in_another_space_gets_404()
    {
        script = new ScriptedModel([new TextOutput("Private.")]);
        var (client, spaceId, runId) = await StartRun("A private question");
        await WaitForRun(client, spaceId, runId, "completed");
        var member = await SeedMember(spaceId, "member", MembershipRole.Member);
        var outsider = await SeedPerson("outsider");
        var asMember = NewClient();
        (await asMember.SignIn(member.Email, member.Password)).EnsureSuccessStatusCode();
        var asOutsider = NewClient();
        (await asOutsider.SignIn(outsider.Email, outsider.Password)).EnsureSuccessStatusCode();

        using var memberStream = await asMember.Open(Events(spaceId, runId));
        using var memberRun = await asMember.Get($"/api/spaces/{spaceId}/runs/{runId}");
        using var outsiderStream = await asOutsider.Open(Events(spaceId, runId));
        using var outsiderOwnSpace = await asOutsider.Open(Events(outsider.SpaceId, runId));
        using var signedOut = await NewClient().Open(Events(spaceId, runId));
        using var notARun = await client.Open(Events(spaceId, Guid.CreateVersion7()));
        using var badCursor = await client.Open($"{Events(spaceId, runId)}?after=-1");
        using var ownStream = await client.Open(Events(spaceId, runId));

        memberStream.StatusCode.Should().Be(HttpStatusCode.NotFound, "a private thread's events are its owner's alone");
        memberRun.StatusCode.Should().Be(HttpStatusCode.NotFound, "a run id tells a member nothing about another member's private thread");
        outsiderStream.StatusCode.Should().Be(HttpStatusCode.NotFound);
        outsiderOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound, "a run id from another space finds nothing in one's own");
        signedOut.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        notARun.StatusCode.Should().Be(HttpStatusCode.NotFound);
        badCursor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ownStream.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Long Streams

    [Fact]
    public async Task No_transaction_is_held_open_for_the_life_of_a_stream_and_a_quiet_stream_sends_heartbeats()
    {
        script = new ScriptedModel([new ToolCallOutput("c1", "test_gate")], [new TextOutput("Done.")]);
        var (client, spaceId, runId) = await StartRun("Hold on");
        using var connection = await client.Open(Events(spaceId, runId));
        await using var body = await connection.Content.ReadAsStreamAsync(Token);
        using var reader = new StreamReader(body);
        var text = new StringBuilder();

        // The stream is open and the run is held at the gate. Nothing happens for a while.
        while (!text.ToString().Contains(RunEventTypes.ToolStarted, StringComparison.Ordinal) || Count(text, ": keep-alive") < 3)
        {
            text.Append(await reader.ReadLineAsync(Token).AsTask().WaitAsync(patience, Token)).Append('\n');
        }

        var longest = await LongestOpenTransaction();
        gate.SetResult();
        await WaitForRun(client, spaceId, runId, "completed");

        Count(text, ": keep-alive").Should().BeGreaterThanOrEqualTo(3, "a quiet stream sends comments so proxies keep it open");
        longest.Should().BeLessThan(TimeSpan.FromMilliseconds(500), "the stream reads in short transactions and holds none while it waits");
    }

    [Fact]
    public async Task The_signal_stream_tells_the_person_their_thread_changed_and_their_run_finished()
    {
        script = new ScriptedModel([new TextOutput("Done.")]);
        Person = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(Person.Email, Person.Password)).EnsureSuccessStatusCode();
        var api = new DroverApi(NewRelay(client));
        List<UserSignal> signals = [];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var watching = Task.Run(async () =>
        {
            await foreach (var signal in api.WatchSignals(Person.SpaceId, cancellationToken: stop.Token))
            {
                lock (signals)
                {
                    signals.Add(signal);
                }
            }
        }, Token);
        await Task.Delay(300, Token);

        var (_, spaceId, runId) = await StartRun("Go", client);
        await Until(() => Snapshot(signals).Any(signal => signal.Kind == UserSignalKinds.RunFinished));
        await stop.CancelAsync();
        await Task.WhenAny(watching);

        Snapshot(signals).Should().Contain(signal => signal.Kind == UserSignalKinds.ThreadChanged && signal.RunId == runId);
        Snapshot(signals).Should().Contain(signal => signal.Kind == UserSignalKinds.RunFinished && signal.RunId == runId);
        spaceId.Should().Be(Person.SpaceId);
    }

    #endregion

    #region Helpers

    private (string Email, string Password, Guid UserId, Guid SpaceId) Person { get; set; }

    private static string Events(Guid spaceId, Guid runId) => $"/api/spaces/{spaceId}/runs/{runId}/events";

    private async Task<(ApiClient Client, Guid SpaceId, Guid RunId)> StartRun(string text, ApiClient? signedIn = null)
    {
        if (signedIn is null)
        {
            Person = await SeedPerson("alice");
        }

        var client = signedIn ?? NewClient();

        if (signedIn is null)
        {
            (await client.SignIn(Person.Email, Person.Password)).EnsureSuccessStatusCode();
        }

        var space = $"/api/spaces/{Person.SpaceId}";
        var connection = await PostFor<ModelConnectionSummary>(client, $"{space}/model-connections", new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));
        var bot = await PostFor<BotSummary>(client, $"{space}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null));
        await GrantGate(bot.Id);
        var thread = await PostFor<ThreadSummary>(client, $"{space}/bots/{bot.Id}/threads", new CreateThreadRequest(null));
        var sent = await PostFor<SendMessageResponse>(client, $"{space}/threads/{thread.Id}/messages", new SendMessageRequest(text));
        return (client, Person.SpaceId, sent.Run.Id);
    }

    /// <summary>A new bot gets only the tools on the default list. The gate is granted here, as a person would in the bot editor.</summary>
    private async Task GrantGate(Guid botId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(Person.SpaceId, Person.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var gateTool = await db.Tools.SingleAsync(tool => tool.Name == "test.gate", Token);
        db.ToolGrants.Add(new ToolGrant { BotId = botId, ToolId = gateTool.Id, Effect = GrantEffect.Allow, GrantedByUserId = Person.UserId });
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    private static async Task<T> PostFor<T>(ApiClient client, string path, object body)
    {
        var response = await client.Post(path, body);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<RunSummary> WaitForRun(ApiClient client, Guid spaceId, Guid runId, string status)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var response = await client.Get($"/api/spaces/{spaceId}/runs/{runId}");
            var run = (await response.Content.ReadFromJsonAsync<RunSummary>(Token))!;

            if (run.Status == status)
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should become {status}; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    private static async Task Until(Func<bool> reached)
    {
        var watch = Stopwatch.StartNew();

        while (!reached())
        {
            (watch.Elapsed < patience).Should().BeTrue("the awaited thing should happen");
            await Task.Delay(25, Token);
        }
    }

    private static List<T> Snapshot<T>(List<T> shared)
    {
        lock (shared)
        {
            return [.. shared];
        }
    }

    private static int Count(StringBuilder text, string wanted) => text.ToString().Split(wanted).Length - 1;

    /// <summary>Reads a stream until the server closes it.</summary>
    private static async Task<List<RunEventResponse>> ReadToEnd(ApiClient client, string path, long? lastEventId = null)
    {
        using var connection = await client.Open(path, lastEventId);
        connection.StatusCode.Should().Be(HttpStatusCode.OK);
        List<RunEventResponse> events = [];

        await foreach (var frame in Frames(connection))
        {
            events.Add(frame.Event);
        }

        return events;
    }

    private static async IAsyncEnumerable<(string Id, string Type, RunEventResponse Event)> Frames(HttpResponseMessage connection)
    {
        await using var body = await connection.Content.ReadAsStreamAsync(Token);

        await foreach (var frame in System.Net.ServerSentEvents.SseParser.Create(body).EnumerateAsync(Token))
        {
            yield return (frame.EventId ?? string.Empty, frame.EventType, JsonSerializer.Deserialize<RunEventResponse>(frame.Data, json)!);
        }
    }

    /// <summary>The age of the oldest transaction any other connection to this test's database has open right now.</summary>
    private async Task<TimeSpan> LongestOpenTransaction()
    {
        var longest = TimeSpan.Zero;

        for (var sample = 0; sample < 10; sample++)
        {
            await using var connection = new NpgsqlConnection(AppConnectionString);
            await connection.OpenAsync(Token);
            await using var command = new NpgsqlCommand(
                """
                SELECT coalesce(max(extract(epoch FROM clock_timestamp() - xact_start)), 0)::float8
                FROM pg_stat_activity
                WHERE datname = current_database() AND pid <> pg_backend_pid() AND xact_start IS NOT NULL
                """,
                connection);
            var seconds = (double)(await command.ExecuteScalarAsync(Token))!;
            longest = TimeSpan.FromSeconds(Math.Max(longest.TotalSeconds, seconds));
            await Task.Delay(100, Token);
        }

        return longest;
    }

    #endregion
}
