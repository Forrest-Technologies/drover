namespace Drover.Presentation.Tests.Shell;

/// <summary>
/// The share inbox outlives any one service container, so Android can take a share before the interface exists and
/// the interface can still read it. It does not outlive the person or the server it was shared to: a share waiting
/// for one sign-in is never shown to the next. The real native registration and server choice are compiled into this
/// project, so these hold without a device or an emulator.
/// </summary>
public sealed class ShareInboxLifetimeTests : IDisposable
{
    #region Public Methods

    [Fact]
    public void AShareOfferedToTheColdStartContainerWaitsInTheRestartedContainer()
    {
        using var coldStart = global::Drover.Native.Shell.AppServices.Build();
        using var restarted = global::Drover.Native.Shell.AppServices.Build();
        var draft = new SharedDraft("Sample words from another app", []);

        coldStart.GetRequiredService<ShareInbox>().Offer(draft);

        restarted.GetRequiredService<ShareInbox>().Waiting.Should().BeSameAs(draft);
    }

    [Fact]
    public async Task AShareStagedByTheColdStartContainerIsPlacedWhileTheInterfaceContainerStarts()
    {
        using var coldStart = global::Drover.Native.Shell.AppServices.Build();
        var draft = new SharedDraft("Sample words from another app", []);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiving = coldStart.GetRequiredService<ShareInbox>().Receive(async () =>
        {
            await held.Task;
            return draft;
        });

        using var restarted = global::Drover.Native.Shell.AppServices.Build();
        held.SetResult();

        (await receiving).Should().BeTrue();
        restarted.GetRequiredService<ShareInbox>().Waiting.Should().BeSameAs(draft);
    }

    [Fact]
    public async Task AShareOfferedWhileSignedInSurvivesALoad()
    {
        using var server = new FakeServer();
        server.Answer("GET", "/api/session", Person());
        var inbox = new ShareInbox();
        var session = new SessionState(server.CreateApi(), inbox);
        await session.Load(TestContext.Current.CancellationToken);
        var draft = new SharedDraft("Sample words from another app", []);

        inbox.Offer(draft);
        await session.Load(TestContext.Current.CancellationToken);

        inbox.Waiting.Should().BeSameAs(draft);
    }

    [Fact]
    public void BeginningASessionForgetsWhatWasShared()
    {
        using var server = new FakeServer();
        var inbox = new ShareInbox();
        var session = new SessionState(server.CreateApi(), inbox);
        inbox.Offer(new SharedDraft("Sample words from another app", []));

        session.Begin(Person());

        inbox.Waiting.Should().BeNull();
    }

    [Fact]
    public async Task EndingTheSessionForgetsWhatWasShared()
    {
        using var server = new FakeServer();
        server.Answer("POST", "/api/sign-out", HttpStatusCode.NoContent);
        var inbox = new ShareInbox();
        var session = new SessionState(server.CreateApi(), inbox);
        inbox.Offer(new SharedDraft("Sample words from another app", []));

        await session.End(TestContext.Current.CancellationToken);

        inbox.Waiting.Should().BeNull();
    }

    [Fact]
    public async Task ChangingTheServerForgetsWhatWasShared()
    {
        var inbox = new ShareInbox();
        inbox.Offer(new SharedDraft("Sample words from another app", []));

        await new DeviceServerChoice(new ServerAddress(), inbox).Change();

        inbox.Waiting.Should().BeNull();
    }

    public void Dispose() => ShareInbox.Process.Discard();

    #endregion

    #region Private Methods

    private static SessionResponse Person() =>
        new(Guid.Parse("0196f1a0-0000-7000-8000-000000000002"), "sample@example.test", "Sample person", []);

    #endregion
}
