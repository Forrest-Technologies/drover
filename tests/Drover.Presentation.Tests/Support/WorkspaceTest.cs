namespace Drover.Presentation.Tests.Support;

/// <summary>
/// A test of the conversation workspace: a server that answers what the test says, a signed-in person, the app's
/// navigator, and the workspace's view model opened at each address the navigator moves to, as the shell does.
/// Every name and value is made up.
/// </summary>
public abstract class WorkspaceTest : IDisposable
{
    #region Private Fields

    private readonly RouteTable<string> routes = new RouteTable<string>().Map(
        nameof(TalkViewModel),
        "/",
        "/talk/{BotId:guid}",
        "/talk/{BotId:guid}/{Rail}",
        "/talk/{BotId:guid}/side/{SideChat}",
        "/talk/{BotId:guid}/activity/{ActivityRunId:guid}",
        "/talk/{BotId:guid}/work/{WorkBotId:guid}/{WorkThreadId:guid}",
        "/threads/{FormerThreadId:guid}");

    #endregion

    #region Constructors

    protected WorkspaceTest(StreamPresence? presence = null)
    {
        Presence = presence;
        var api = Server.CreateApi(presence);
        Session = new(api, Inbox);
        Toasts = new(TimeProvider.System);
        Talk = new(api, Session, Navigator, Toasts, Inbox, Ui, NullLoggerFactory.Instance);
        Navigator.Navigated += (address, kind) =>
        {
            if (routes.Match(address) is { } match)
            {
                _ = Talk.Open(TalkAddress.From(match));
            }
        };
        Server.Answer("GET", "/api/session", Person);
    }

    #endregion

    #region Properties

    protected static Guid SpaceId { get; } = Guid.Parse("0196f1a0-0000-7000-8000-000000000001");

    protected static string Space { get; } = $"/api/spaces/{SpaceId}";

    protected static SessionResponse Person { get; } = new(
        Guid.Parse("0196f1a0-0000-7000-8000-000000000002"),
        "sam@example.org",
        "Sam Sample",
        [new(SpaceId, "personal", "Sam Sample", "sam", "owner")]);

    protected FakeServer Server { get; } = new();

    /// <summary>Whether the app is on screen, for a test that hides it and shows it again. Null when it is not asked
    /// for.</summary>
    protected StreamPresence? Presence { get; }

    protected UiLoop Ui { get; } = new();

    protected Navigator Navigator { get; } = new();

    protected ShareInbox Inbox { get; } = new();

    protected SessionState Session { get; }

    protected ToastService Toasts { get; }

    protected TalkViewModel Talk { get; }

    /// <summary>Where the app is, as the web client's tests read it: without the leading slash.</summary>
    protected string Address => Navigator.Current.TrimStart('/');

    #endregion

    #region Public Methods

    public void Dispose()
    {
        Talk.Dispose();
        Ui.Dispose();
        Server.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Goes to an address of the workspace, as following a link does.</summary>
    protected Task Go(string address) => Ui.Run(() => Navigator.NavigateTo(address));

    /// <summary>Does something on the interface's thread, as a person's press does, and waits for all of it.</summary>
    protected Task Do(Func<Task> act) => Ui.Run(act);

    protected Task Until(Action assertion) => Ui.Until(assertion);

    /// <summary>Takes one step back on the interface's thread, as a device's Back does, and checks what it did.</summary>
    protected Task StepBack(BackStep expected) => Ui.Run(() => Navigator.StepBack().Should().Be(expected));

    /// <summary>Writes in a conversation's field and sends, as Enter does.</summary>
    protected Task Send(Func<ConversationViewModel> pane, string text) => Do(() =>
    {
        pane().Composer.Text = text;
        return pane().Composer.Send();
    });

    #endregion
}
