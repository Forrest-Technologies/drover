namespace Drover.Presentation.Tests.Support;

/// <summary>
/// What the tests of the conversation's pieces share: the fake server, a signed-in person, the toasts, the recorded
/// event logs, and a way to wait for what a view model does after a request comes back.
/// </summary>
public sealed class ChatBench : IDisposable
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan pause = TimeSpan.FromMilliseconds(10);

    private readonly ServiceProvider services;

    #endregion

    #region Constructors

    public ChatBench(string role = "owner", TimeProvider? time = null)
    {
        var api = Server.CreateApi();
        Session = new(api);
        Session.Begin(new(
            Guid.Parse("0196f1a0-0000-7000-8000-000000000002"),
            "sam@example.org",
            "Sam Sample",
            [new(SpaceId, "personal", "Sam Sample", "sam", role)]));
        Toasts = new(time ?? TimeProvider.System);
        services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(api)
            .AddSingleton(Session)
            .AddSingleton(Toasts)
            .AddSingleton(time ?? TimeProvider.System)
            .BuildServiceProvider();
    }

    #endregion

    #region Properties

    public static Guid SpaceId { get; } = Guid.Parse("0196f1a0-0000-7000-8000-000000000001");

    /// <summary>The start of every address of the sample space.</summary>
    public static string Space { get; } = $"/api/spaces/{SpaceId}";

    public FakeServer Server { get; } = new();

    public SessionState Session { get; }

    public ToastService Toasts { get; }

    #endregion

    #region Public Methods

    public void Dispose()
    {
        services.Dispose();
        Server.Dispose();
    }

    /// <summary>A view model, its constructor filled from the bench's services, as a control fills it from the app's.</summary>
    public T Make<T>() => ActivatorUtilities.CreateInstance<T>(services);

    /// <summary>An event log or a thread's messages, recorded from a real server: the web client's fixtures, linked.</summary>
    public static T Load<T>(string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), json)!;

    /// <summary>Waits until what a view model does after its requests come back has happened.</summary>
    public static async Task Eventually(Action assertion)
    {
        var until = DateTimeOffset.UtcNow + patience;

        while (Record.Exception(assertion) is not null && DateTimeOffset.UtcNow < until)
        {
            await Task.Delay(pause, TestContext.Current.CancellationToken);
        }

        assertion();
    }

    #endregion
}
