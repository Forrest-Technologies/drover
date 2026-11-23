namespace Drover.Presentation.Tests.Shell;

/// <summary>
/// Android copies a share's files before it offers them, and the person or the server can change meanwhile. These
/// tests hold the copy open across each change and then let it finish: a share that arrived for someone else must
/// never be placed for the next person, and its staged copies must not be left behind.
/// </summary>
public sealed class ShareInboxStagingTests
{
    #region Public Methods

    [Fact]
    public async Task AShareStagedWhileNothingChangesIsPlaced()
    {
        var inbox = new ShareInbox();
        var file = new StagedSampleFile();

        var (placed, draft) = await ReceiveAcross(inbox, file, () => Task.CompletedTask);

        placed.Should().BeTrue();
        inbox.Waiting.Should().BeSameAs(draft);
        file.Released.Should().BeFalse();
    }

    [Fact]
    public async Task AShareStagedAcrossBeginningASessionIsNeverPlaced()
    {
        using var server = new FakeServer();
        var inbox = new ShareInbox();
        var session = new SessionState(server.CreateApi(), inbox);
        var file = new StagedSampleFile();

        var (placed, _) = await ReceiveAcross(inbox, file, () =>
        {
            session.Begin(Person());
            return Task.CompletedTask;
        });

        placed.Should().BeFalse();
        inbox.Waiting.Should().BeNull();
        file.Released.Should().BeTrue();
    }

    [Fact]
    public async Task AShareStagedAcrossEndingTheSessionIsNeverPlaced()
    {
        using var server = new FakeServer();
        server.Answer("POST", "/api/sign-out", HttpStatusCode.NoContent);
        var inbox = new ShareInbox();
        var session = new SessionState(server.CreateApi(), inbox);
        var file = new StagedSampleFile();

        var (placed, _) = await ReceiveAcross(inbox, file, () => session.End(TestContext.Current.CancellationToken));

        placed.Should().BeFalse();
        inbox.Waiting.Should().BeNull();
        file.Released.Should().BeTrue();
    }

    [Fact]
    public async Task AShareStagedAcrossChangingTheServerIsNeverPlaced()
    {
        var inbox = new ShareInbox();
        var file = new StagedSampleFile();

        var choice = new DeviceServerChoice(new ServerAddress(), inbox);

        var (placed, _) = await ReceiveAcross(inbox, file, choice.Change);

        placed.Should().BeFalse();
        inbox.Waiting.Should().BeNull();
        file.Released.Should().BeTrue();
    }

    [Fact]
    public async Task ABotChosenAfterAStaleShareWasDroppedTakesNothing()
    {
        using var server = new FakeServer();
        var inbox = new ShareInbox();
        var session = new SessionState(server.CreateApi(), inbox);
        var bot = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

        await ReceiveAcross(inbox, new StagedSampleFile(), () =>
        {
            session.Begin(Person());
            return Task.CompletedTask;
        });
        inbox.SendTo(bot);

        inbox.TakeFor(bot).Should().BeNull();
    }

    [Fact]
    public async Task AShareStagedAfterTheChangeIsPlacedForTheNewPerson()
    {
        using var server = new FakeServer();
        var inbox = new ShareInbox();
        var session = new SessionState(server.CreateApi(), inbox);
        session.Begin(Person());
        var file = new StagedSampleFile();

        var (placed, draft) = await ReceiveAcross(inbox, file, () => Task.CompletedTask);

        placed.Should().BeTrue();
        inbox.Waiting.Should().BeSameAs(draft);
    }

    [Fact]
    public void BeginningASessionReleasesTheStagedCopiesOfTheShareWaiting()
    {
        using var server = new FakeServer();
        var inbox = new ShareInbox();
        var session = new SessionState(server.CreateApi(), inbox);
        var file = new StagedSampleFile();
        inbox.Offer(new SharedDraft("Sample words from another app", [file]));

        session.Begin(Person());

        file.Released.Should().BeTrue();
    }

    [Fact]
    public async Task EndingTheSessionReleasesTheStagedCopiesOfTheShareWaiting()
    {
        using var server = new FakeServer();
        server.Answer("POST", "/api/sign-out", HttpStatusCode.NoContent);
        var inbox = new ShareInbox();
        var session = new SessionState(server.CreateApi(), inbox);
        var file = new StagedSampleFile();
        inbox.Offer(new SharedDraft("Sample words from another app", [file]));

        await session.End(TestContext.Current.CancellationToken);

        file.Released.Should().BeTrue();
    }

    [Fact]
    public async Task ChangingTheServerReleasesTheStagedCopiesOfTheShareWaiting()
    {
        var inbox = new ShareInbox();
        var file = new StagedSampleFile();
        inbox.Offer(new SharedDraft("Sample words from another app", [file]));

        await new DeviceServerChoice(new ServerAddress(), inbox).Change();

        file.Released.Should().BeTrue();
    }

    [Fact]
    public void AShareTakenByABotsConversationKeepsItsFiles()
    {
        var inbox = new ShareInbox();
        var bot = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");
        var file = new StagedSampleFile();
        inbox.Offer(new SharedDraft("Sample words from another app", [file]));
        inbox.SendTo(bot);

        var taken = inbox.TakeFor(bot);
        inbox.Reset();

        taken!.Files.Should().ContainSingle().Which.Should().BeSameAs(file);
        file.Released.Should().BeFalse();
    }

    [Fact]
    public void ADiscardedShareReleasesItsFiles()
    {
        var inbox = new ShareInbox();
        var file = new StagedSampleFile();
        inbox.Offer(new SharedDraft("Sample words from another app", [file]));

        inbox.Discard();

        file.Released.Should().BeTrue();
        inbox.Waiting.Should().BeNull();
    }

    [Fact]
    public async Task AShareTheShareScreenDiscardsDoesNotEndAShareStillBeingStaged()
    {
        var inbox = new ShareInbox();
        var file = new StagedSampleFile();

        var (placed, _) = await ReceiveAcross(inbox, file, () =>
        {
            inbox.Discard();
            return Task.CompletedTask;
        });

        placed.Should().BeTrue();
        file.Released.Should().BeFalse();
    }

    [Fact]
    public async Task AShareThatReplacesOneNeverPlacedReleasesTheOldFiles()
    {
        var inbox = new ShareInbox();
        var old = new StagedSampleFile();
        inbox.Offer(new SharedDraft("Sample words from another app", [old]));

        await ReceiveAcross(inbox, new StagedSampleFile(), () => Task.CompletedTask);

        old.Released.Should().BeTrue();
    }

    #endregion

    #region Private Methods

    private static async Task<(bool Placed, SharedDraft Draft)> ReceiveAcross(
        ShareInbox inbox,
        StagedSampleFile file,
        Func<Task> change)
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var draft = new SharedDraft("Sample words from another app", [file]);

        // The receive reads the inbox's generation, then waits at the copy. Nothing runs past it until released.
        var receiving = inbox.Receive(async () =>
        {
            await held.Task;
            return draft;
        });
        receiving.IsCompleted.Should().BeFalse();

        await change();
        held.SetResult();

        return (await receiving, draft);
    }

    private static SessionResponse Person() =>
        new(Guid.Parse("0196f1a0-0000-7000-8000-000000000002"), "sample@example.test", "Sample person", []);

    #endregion

    #region Nested Types

    private sealed class StagedSampleFile : IPickedFile, IDisposable
    {
        #region Properties

        public bool Released { get; private set; }

        public string Name => "sample.txt";

        public long Size => 0;

        #endregion

        #region Interface Implementations

        public Task<Stream> OpenRead(long maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream());

        public void Dispose() => Released = true;

        #endregion
    }

    #endregion
}
