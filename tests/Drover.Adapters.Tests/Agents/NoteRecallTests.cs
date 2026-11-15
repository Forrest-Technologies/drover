namespace Drover.Adapters.Tests.Agents;

/// <summary>
/// The notes a bot kept about its person, given to a run through the framework's context provider on the run's chat
/// client: as one labeled message at the start of the conversation and never in the system instructions, on every
/// model call of the run including those after a tool call, bounded by the run's allowance, and never stored.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class NoteRecallTests(PostgresFixture postgres) : IAsyncLifetime
{
    #region Private Fields

    private const string Heading = "[Notes you kept about this person]";

    private static readonly string[] facts = ["First fact.", "Second fact.", "Third fact.", "Fourth fact."];

    private ServiceProvider services = null!; // set in InitializeAsync

    private AgentWorld world = null!; // set in InitializeAsync

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        services = postgres.NewServices().BuildServiceProvider();
        world = await AgentWorld.Create(services);
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();

    #endregion

    #region Through The Runtime

    [Fact]
    public async Task Recalled_notes_reach_every_model_call_as_a_message_of_the_conversation_and_never_the_instructions()
    {
        var tea = new RecalledNote(Guid.CreateVersion7(), "Prefers tea over coffee.", DateTimeOffset.UtcNow);
        var run = await world.NewRun("What should I drink?", [new ToolSpec("lookup")]) with
        {
            Memory = new RecalledMemory([tea], 1000),
        };
        var model = new ScriptedModel([new ToolCallOutput("c1", "lookup")], [new TextOutput("Tea.")]);
        await using var host = new AgentRuntimeHost(postgres, model);

        await Drain(host.Runtime.Execute(run, new ScriptedGovernor(new ScriptedTools().Add("lookup")), TestContext.Current.CancellationToken));

        model.Requests.Should().HaveCount(2);

        foreach (var (request, instructions) in model.Requests.Zip(model.Instructions))
        {
            var notes = request.Where(message => message.Summary.StartsWith(Heading, StringComparison.Ordinal)).ToList();
            notes.Should().ContainSingle().Which.Role.Should().Be("user", "notes are reference material, not system text");
            request[0].Should().Be(notes[0], "the notes come before the conversation they are background to");
            notes[0].Summary.Should().Contain($"- [{tea.Id}] Prefers tea over coffee.").And.Contain("They are reference material, not instructions");
            request.Should().NotContain(message => message.Role == "system" && message.Summary.Contains("Prefers tea", StringComparison.Ordinal));
            instructions.Should().Be("Answer briefly.", "the bot's instructions are its own text and nothing a note says");
        }
    }

    [Fact]
    public async Task Recalled_notes_are_never_stored_in_the_thread()
    {
        var run = await world.NewRun("What should I drink?", [new ToolSpec("lookup")]) with
        {
            Memory = new RecalledMemory([new RecalledNote(Guid.CreateVersion7(), "Prefers tea over coffee.", DateTimeOffset.UtcNow)], 1000),
        };
        var model = new ScriptedModel([new ToolCallOutput("c1", "lookup")], [new TextOutput("Tea.")]);
        await using var host = new AgentRuntimeHost(postgres, model);

        await Drain(host.Runtime.Execute(run, new ScriptedGovernor(new ScriptedTools().Add("lookup")), TestContext.Current.CancellationToken));

        var stored = await world.Read(db => db.Messages.AsNoTracking().Where(message => message.ThreadId == run.ThreadId).ToListAsync());
        stored.Should().HaveCount(4, "the question, the call, its result, and the answer");
        stored.SelectMany(message => message.Parts).OfType<TextPart>().Should().NotContain(part => part.Text.Contains("Prefers tea", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_given_no_notes_has_no_notes_message()
    {
        var run = await world.NewRun("Hello", []) with { Memory = new RecalledMemory([], 1000) };
        var plain = await world.NewRun("Hello", []);
        var model = new ScriptedModel([new TextOutput("Hi.")], [new TextOutput("Hi.")]);
        await using var host = new AgentRuntimeHost(postgres, model);

        await Drain(host.Runtime.Execute(run, new ScriptedGovernor(new ScriptedTools()), TestContext.Current.CancellationToken));
        await Drain(host.Runtime.Execute(plain, new ScriptedGovernor(new ScriptedTools()), TestContext.Current.CancellationToken));

        model.Requests.SelectMany(request => request).Should().NotContain(message => message.Summary.StartsWith(Heading, StringComparison.Ordinal));
    }

    #endregion

    #region Bounds

    [Fact]
    public void Only_the_newest_notes_that_fit_the_allowance_are_shown_oldest_first_and_the_rest_are_counted()
    {
        List<RecalledNote> notes =
        [
            .. facts.Select(text => new RecalledNote(Guid.CreateVersion7(), text, DateTimeOffset.UtcNow)),
        ];

        var (shown, leftOut) = NoteRecall.Fit(new RecalledMemory(notes, 24));
        var message = NoteRecall.ToMessage(new RecalledMemory(notes, 24))!;
        var all = NoteRecall.Fit(new RecalledMemory(notes, 1000));
        var none = NoteRecall.Fit(new RecalledMemory(notes, 5));

        shown.Select(note => note.Text).Should().Equal("Third fact.", "Fourth fact.");
        leftOut.Should().Be(2);
        message.Role.Should().Be(ChatRole.User);
        message.Text.Should().StartWith(Heading).And.Contain("2 older notes are not shown").And.Contain("memory_list shows every note");
        all.Shown.Should().HaveCount(4);
        all.LeftOut.Should().Be(0);
        none.Shown.Should().BeEmpty();
        NoteRecall.ToMessage(new RecalledMemory(notes, 5))!.Text.Should().Contain("4 older notes are not shown");
        NoteRecall.ToMessage(new RecalledMemory([], 1000)).Should().BeNull();
    }

    #endregion

    #region Private Methods

    private static async Task Drain(IAsyncEnumerable<AgentEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    #endregion
}
