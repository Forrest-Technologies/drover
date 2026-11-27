namespace Drover.Adapters.Conformance.Memory;

/// <summary>
/// A memory provider rigged for a conformance scenario: the provider, the secret key configured for it, the opaque
/// identifiers the adapter sent on the wire, and a switch to simulate provider failures.
/// </summary>
public sealed class MemoryProviderUnderTest(
    IMemoryProvider provider,
    string apiKey,
    Func<IReadOnlyList<string>> sentIdentifiers,
    Action<MemoryFailure?> failWith,
    Func<ValueTask>? dispose = null) : IAsyncDisposable
{
    #region Properties

    public IMemoryProvider Provider => provider;

    public string ApiKey => apiKey;

    #endregion

    #region Public Methods

    public IReadOnlyList<string> SentIdentifiers() => sentIdentifiers();

    public void FailWith(MemoryFailure? failure) => failWith(failure);

    #endregion

    #region Interface Implementations

    public ValueTask DisposeAsync() => dispose is null ? ValueTask.CompletedTask : dispose();

    #endregion
}


/// <summary>
/// What every <see cref="IMemoryProvider"/> must do. An adapter inherits this class and supplies a
/// <see cref="MemoryProviderUnderTest"/>. The suite proves isolation per bot, per person, and per space, bounded
/// recall, conversation and person forgetting, GUID-only identifiers, and secret-free exception messages.
/// </summary>
public abstract class MemoryProviderConformance
{
    #region Properties

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Isolation

    [Fact]
    public async Task The_provider_has_a_kind()
    {
        await using var underTest = await CreateProvider(NewApiKey());

        underTest.Provider.Kind.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task What_bot_A_was_told_by_a_person_is_not_recalled_for_bot_B_with_the_same_person()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var spaceId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var botA = new MemoryScope(spaceId, Guid.CreateVersion7(), personId, Guid.CreateVersion7());
        var botB = new MemoryScope(spaceId, Guid.CreateVersion7(), personId, Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await underTest.Provider.Remember(
            botA,
            [
                new MemoryTurn(MemorySpeaker.Person, "I prefer oolong tea in the morning.", now),
                new MemoryTurn(MemorySpeaker.Bot, "I will keep oolong tea in mind.", now.AddSeconds(1)),
            ],
            Token);

        var recalledForA = await underTest.Provider.Recall(botA, new MemoryRecallRequest("What do I drink?", 4000), Token);
        var recalledForB = await underTest.Provider.Recall(botB, new MemoryRecallRequest("What do I drink?", 4000), Token);

        CombinedText(recalledForA).Should().Contain("oolong");
        CombinedText(recalledForB).Should().NotContain("oolong");
    }

    [Fact]
    public async Task What_person_1_said_is_never_recalled_for_person_2()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var spaceId = Guid.CreateVersion7();
        var botId = Guid.CreateVersion7();
        var person1 = new MemoryScope(spaceId, botId, Guid.CreateVersion7(), Guid.CreateVersion7());
        var person2 = new MemoryScope(spaceId, botId, Guid.CreateVersion7(), Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await underTest.Provider.Remember(
            person1,
            [
                new MemoryTurn(MemorySpeaker.Person, "My favorite trail is Cascade Ridge.", now),
                new MemoryTurn(MemorySpeaker.Bot, "Cascade Ridge sounds great.", now.AddSeconds(1)),
            ],
            Token);

        var recalledFor1 = await underTest.Provider.Recall(person1, new MemoryRecallRequest("Where do I hike?", 4000), Token);
        var recalledFor2 = await underTest.Provider.Recall(person2, new MemoryRecallRequest("Where do I hike?", 4000), Token);

        CombinedText(recalledFor1).Should().Contain("Cascade Ridge");
        CombinedText(recalledFor2).Should().NotContain("Cascade Ridge");
    }

    [Fact]
    public async Task What_was_said_in_space_1_is_never_recalled_in_space_2()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var botId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var space1 = new MemoryScope(Guid.CreateVersion7(), botId, personId, Guid.CreateVersion7());
        var space2 = new MemoryScope(Guid.CreateVersion7(), botId, personId, Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await underTest.Provider.Remember(
            space1,
            [
                new MemoryTurn(MemorySpeaker.Person, "Our office is in Bergen.", now),
                new MemoryTurn(MemorySpeaker.Bot, "Noted Bergen.", now.AddSeconds(1)),
            ],
            Token);

        var recalledIn1 = await underTest.Provider.Recall(space1, new MemoryRecallRequest("Where is the office?", 4000), Token);
        var recalledIn2 = await underTest.Provider.Recall(space2, new MemoryRecallRequest("Where is the office?", 4000), Token);

        CombinedText(recalledIn1).Should().Contain("Bergen");
        CombinedText(recalledIn2).Should().NotContain("Bergen");
    }

    #endregion

    #region Bounds And Forgetting

    [Fact]
    public async Task Recall_respects_MaxCharacters()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var scope = new MemoryScope(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < 8; index++)
        {
            await underTest.Provider.Remember(
                scope,
                [
                    new MemoryTurn(MemorySpeaker.Person, $"Fact number {index}: prefers detail level {index} with extra context for length.", now.AddSeconds(index * 2)),
                    new MemoryTurn(MemorySpeaker.Bot, $"Acknowledged detail level {index}.", now.AddSeconds((index * 2) + 1)),
                ],
                Token);
        }

        const int Limit = 120;
        var recalled = await underTest.Provider.Recall(scope, new MemoryRecallRequest("What do you know?", Limit), Token);

        recalled.Should().NotBeNull();
        var totalCharacters = (recalled!.Summary?.Length ?? 0) + recalled.Facts.Sum(fact => fact.Length);
        totalCharacters.Should().BeLessThanOrEqualTo(Limit);
    }

    [Fact]
    public async Task A_forgotten_conversation_contributes_nothing_afterwards()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var spaceId = Guid.CreateVersion7();
        var botId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var forgottenThread = new MemoryScope(spaceId, botId, personId, Guid.CreateVersion7());
        var keptThread = new MemoryScope(spaceId, botId, personId, Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await underTest.Provider.Remember(
            forgottenThread,
            [
                new MemoryTurn(MemorySpeaker.Person, "I am planning a surprise party on Elm Street.", now),
                new MemoryTurn(MemorySpeaker.Bot, "I will keep that in mind.", now.AddSeconds(1)),
            ],
            Token);
        await underTest.Provider.Remember(
            keptThread,
            [
                new MemoryTurn(MemorySpeaker.Person, "I build wooden kayaks on weekends.", now.AddSeconds(2)),
                new MemoryTurn(MemorySpeaker.Bot, "That is a great craft.", now.AddSeconds(3)),
            ],
            Token);

        await underTest.Provider.ForgetConversation(forgottenThread, Token);
        var recalled = await underTest.Provider.Recall(keptThread, new MemoryRecallRequest("What do you know?", 4000), Token);

        CombinedText(recalled).Should().NotContain("Elm Street");
        CombinedText(recalled).Should().Contain("kayaks");
    }

    [Fact]
    public async Task A_forgotten_person_gets_nothing_back()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var spaceId = Guid.CreateVersion7();
        var botA = Guid.CreateVersion7();
        var botB = Guid.CreateVersion7();
        var person1 = Guid.CreateVersion7();
        var person2 = Guid.CreateVersion7();
        var person1BotA = new MemoryScope(spaceId, botA, person1, Guid.CreateVersion7());
        var person1BotB = new MemoryScope(spaceId, botB, person1, Guid.CreateVersion7());
        var person2BotA = new MemoryScope(spaceId, botA, person2, Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await underTest.Provider.Remember(
            person1BotA,
            [new MemoryTurn(MemorySpeaker.Person, "I play cello in a quartet.", now)],
            Token);
        await underTest.Provider.Remember(
            person1BotB,
            [new MemoryTurn(MemorySpeaker.Person, "I live near Harbor Point.", now.AddSeconds(1))],
            Token);
        await underTest.Provider.Remember(
            person2BotA,
            [new MemoryTurn(MemorySpeaker.Person, "I collect vintage maps.", now.AddSeconds(2))],
            Token);

        await underTest.Provider.ForgetPerson(new MemoryPersonScope(spaceId, person1), Token);

        CombinedText(await underTest.Provider.Recall(person1BotA, new MemoryRecallRequest("Anything?", 4000), Token))
            .Should().BeEmpty();
        CombinedText(await underTest.Provider.Recall(person1BotB, new MemoryRecallRequest("Anything?", 4000), Token))
            .Should().BeEmpty();
        CombinedText(await underTest.Provider.Recall(person2BotA, new MemoryRecallRequest("Anything?", 4000), Token))
            .Should().Contain("vintage maps");
    }

    [Fact]
    public async Task Forgetting_twice_or_forgetting_what_was_never_remembered_succeeds()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var spaceId = Guid.CreateVersion7();
        var botId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var rememberedScope = new MemoryScope(spaceId, botId, personId, Guid.CreateVersion7());
        var unknownScope = new MemoryScope(spaceId, botId, personId, Guid.CreateVersion7());
        var unknownPerson = new MemoryPersonScope(spaceId, Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await underTest.Provider.Remember(
            rememberedScope,
            [new MemoryTurn(MemorySpeaker.Person, "I bake sourdough on Sundays.", now)],
            Token);

        await underTest.Provider.ForgetConversation(rememberedScope, Token);
        await underTest.Provider.ForgetConversation(rememberedScope, Token);
        await underTest.Provider.ForgetConversation(unknownScope, Token);

        var personScope = new MemoryPersonScope(spaceId, personId);
        await underTest.Provider.ForgetPerson(personScope, Token);
        await underTest.Provider.ForgetPerson(personScope, Token);
        await underTest.Provider.ForgetPerson(unknownPerson, Token);

        CombinedText(await underTest.Provider.Recall(rememberedScope, new MemoryRecallRequest("Anything?", 4000), Token))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Forgetting_a_person_in_one_space_keeps_what_they_said_in_another()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var botId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var space1 = new MemoryScope(Guid.CreateVersion7(), botId, personId, Guid.CreateVersion7());
        var space2 = new MemoryScope(Guid.CreateVersion7(), botId, personId, Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await underTest.Provider.Remember(
            space1,
            [new MemoryTurn(MemorySpeaker.Person, "In space one I prefer dark roast coffee.", now)],
            Token);
        await underTest.Provider.Remember(
            space2,
            [new MemoryTurn(MemorySpeaker.Person, "In space two I prefer green tea.", now.AddSeconds(1))],
            Token);

        await underTest.Provider.ForgetPerson(new MemoryPersonScope(space1.SpaceId, personId), Token);

        CombinedText(await underTest.Provider.Recall(space1, new MemoryRecallRequest("Drinks?", 4000), Token))
            .Should().BeEmpty();
        CombinedText(await underTest.Provider.Recall(space2, new MemoryRecallRequest("Drinks?", 4000), Token))
            .Should().Contain("green tea");
    }

    #endregion

    #region Identifiers And Secrets

    [Fact]
    public async Task Ids_sent_to_the_provider_are_GUIDs_only()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var scope = new MemoryScope(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await underTest.Provider.Remember(
            scope,
            [
                new MemoryTurn(MemorySpeaker.Person, "Hello there.", now),
                new MemoryTurn(MemorySpeaker.Bot, "Hello back.", now.AddSeconds(1)),
            ],
            Token);
        await underTest.Provider.Recall(scope, new MemoryRecallRequest("Hello?", 4000), Token);
        await underTest.Provider.ForgetConversation(scope, Token);
        await underTest.Provider.ForgetPerson(new MemoryPersonScope(scope.SpaceId, scope.PersonId), Token);

        var sent = underTest.SentIdentifiers();
        sent.Should().NotBeEmpty("the test harness must record the identifiers sent to the provider");
        sent.Should().OnlyContain(id => IsGuidIdentifier(id), "every identifier sent to the memory provider must be a Drover GUID");
    }

    [Theory]
    [InlineData(MemoryFailure.InvalidKey)]
    [InlineData(MemoryFailure.RetryLater)]
    [InlineData(MemoryFailure.OutOfCredit)]
    [InlineData(MemoryFailure.Unreachable)]
    [InlineData(MemoryFailure.Rejected)]
    public async Task The_key_is_in_no_exception_message(MemoryFailure failure)
    {
        var apiKey = NewApiKey();
        await using var underTest = await CreateProvider(apiKey);
        underTest.FailWith(failure);
        var scope = new MemoryScope(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        var remember = () => underTest.Provider.Remember(scope, [new MemoryTurn(MemorySpeaker.Person, "Secret turn.", now)], Token);
        var recall = () => underTest.Provider.Recall(scope, new MemoryRecallRequest("Question?", 4000), Token);
        var forgetConversation = () => underTest.Provider.ForgetConversation(scope, Token);
        var forgetPerson = () => underTest.Provider.ForgetPerson(new MemoryPersonScope(scope.SpaceId, scope.PersonId), Token);

        foreach (var call in new Func<Task>[] { remember, recall, forgetConversation, forgetPerson })
        {
            var thrown = (await call.Should().ThrowAsync<MemoryProviderException>()).Which;
            thrown.Failure.Should().Be(failure);
            thrown.Message.Should().NotBeNullOrWhiteSpace().And.NotContain(apiKey);
            thrown.ToString().Should().NotContain(apiKey);
        }
    }

    [Fact]
    public async Task Cancellation_throws_and_is_never_reported_as_a_provider_failure()
    {
        await using var underTest = await CreateProvider(NewApiKey());
        var scope = new MemoryScope(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var recall = () => underTest.Provider.Recall(scope, new MemoryRecallRequest("Hello?", 4000), cancelled.Token);

        await recall.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Protected Methods

    /// <summary>Creates a fresh provider under test using <paramref name="apiKey"/>.</summary>
    protected abstract Task<MemoryProviderUnderTest> CreateProvider(string apiKey);

    #endregion

    #region Private Methods

    private static string NewApiKey() => $"mem-test-{Convert.ToHexString(RandomNumberGenerator.GetBytes(20))}";

    private static string CombinedText(MemoryRecall? recall) =>
        recall is null ? string.Empty : string.Join("\n", [.. recall.Facts, recall.Summary ?? string.Empty]).Trim();

    /// <summary>
    /// True when the identifier is a bare GUID or a colon-delimited composite of GUIDs (for example
    /// <c>{spaceId}:{botId}:{personId}</c>), containing no name, email, or bot name.
    /// </summary>
    private static bool IsGuidIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Split(':', StringSplitOptions.None).All(segment => Guid.TryParse(segment, out _));

    #endregion
}
