namespace Drover.Presentation.Tests.Support;

/// <summary>
/// A view model against the fake server, with the services the app registers, and the sample person and space the
/// web client's screen tests use, so the cases ported from them read the same.
/// </summary>
public abstract class ViewModelTest : IDisposable
{
    #region Private Fields

    private readonly ServiceProvider services;

    #endregion

    #region Constructors

    protected ViewModelTest()
    {
        Session = new(Server.CreateApi(), Inbox);
        services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Server.CreateApi())
            .AddSingleton(Session)
            .AddSingleton(Navigator)
            .AddSingleton(Toasts)
            .AddSingleton(Inbox)
            .BuildServiceProvider();
    }

    #endregion

    #region Properties

    protected static Guid SpaceId { get; } = Guid.Parse("0196f1a0-0000-7000-8000-000000000001");

    protected static SessionResponse Person { get; } = new(
        Guid.Parse("0196f1a0-0000-7000-8000-000000000002"),
        "sam@example.org",
        "Sam Sample",
        [new(SpaceId, "personal", "Sam Sample", "sam", "owner")]);

    protected FakeServer Server { get; } = new();

    protected Navigator Navigator { get; } = new();

    protected ToastService Toasts { get; } = new(TimeProvider.System);

    protected SessionState Session { get; }

    /// <summary>What another app shared, waiting for a bot: the one inbox both clients register.</summary>
    protected ShareInbox Inbox { get; } = new();

    #endregion

    #region Public Methods

    public void Dispose()
    {
        services.Dispose();
        Server.Dispose();
        GC.SuppressFinalize(this);
    }

    protected void SignedIn() => Server.Answer("GET", "/api/session", Person);

    protected T Make<T>() => ActivatorUtilities.CreateInstance<T>(services);

    /// <summary>Waits until the condition holds, for work that finishes after a pause of its own, such as a check while typing.</summary>
    protected static async Task Eventually(Func<bool> condition)
    {
        for (var tries = 0; tries < 100 && !condition(); tries++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        condition().Should().BeTrue("the condition should hold once the work is done");
    }

    #endregion
}
