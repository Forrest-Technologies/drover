namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// What a view model of a screen needs to be tried: the stand-in server, the navigator, the session, and the way the
/// app makes a view model, filling its constructor from its services. Every name and value is a sample.
/// </summary>
public abstract class ServerScreenTest : IDisposable
{
    #region Private Fields

    private ServiceProvider? provider;

    #endregion

    #region Constructors

    protected ServerScreenTest()
    {
        Session = new(Server.CreateApi());
        Services.AddLogging()
            .AddSingleton(Server.CreateApi())
            .AddSingleton(Session)
            .AddSingleton(Navigator);
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

    protected SessionState Session { get; }

    /// <summary>The app's services. Add to them before the first view model is made.</summary>
    protected ServiceCollection Services { get; } = [];

    #endregion

    #region Public Methods

    public void Dispose()
    {
        provider?.Dispose();
        Server.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Waits for something a view model does on its own while a request is held, up to ten seconds.</summary>
    protected static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    protected T Make<T>() => ActivatorUtilities.CreateInstance<T>(provider ??= Services.BuildServiceProvider());

    protected void SignedIn() => Server.Answer("GET", "/api/session", Person);

    protected void SignedInAs(string role) =>
        Server.Answer("GET", "/api/session", Person with { Spaces = [Person.Spaces[0] with { Role = role }] });

    #endregion
}
