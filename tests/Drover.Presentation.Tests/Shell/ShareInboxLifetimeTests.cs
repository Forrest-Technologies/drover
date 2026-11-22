namespace Drover.Presentation.Tests.Shell;

/// <summary>
/// The share inbox outlives any one service container: Android takes a share with the cold-start container's
/// services before the interface exists, and the interface builds its own container when it starts. The real native
/// registration is compiled into this project, so this holds the lifetime without a device or an emulator.
/// </summary>
public sealed class ShareInboxLifetimeTests
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

    #endregion
}
