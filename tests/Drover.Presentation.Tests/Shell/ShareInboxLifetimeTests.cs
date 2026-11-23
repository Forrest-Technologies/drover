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
    public async Task EndingTheSessionForgetsWhatWasShared()
    {
        using var server = new FakeServer();
        server.Answer("POST", "/api/sign-out", HttpStatusCode.NoContent);
        var session = new SessionState(server.CreateApi());
        ShareInbox.Process.Offer(new SharedDraft("Sample words from another app", []));

        await session.End(TestContext.Current.CancellationToken);

        ShareInbox.Process.Waiting.Should().BeNull();
    }

    [Fact]
    public async Task ChangingTheServerForgetsWhatWasShared()
    {
        ShareInbox.Process.Offer(new SharedDraft("Sample words from another app", []));

        await new DeviceServerChoice(new ServerAddress()).Change();

        ShareInbox.Process.Waiting.Should().BeNull();
    }

    public void Dispose() => ShareInbox.Process.Discard();

    #endregion
}
