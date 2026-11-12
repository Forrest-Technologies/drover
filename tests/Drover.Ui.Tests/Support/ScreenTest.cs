namespace Drover.Ui.Tests.Support;

/// <summary>
/// A screen rendered against the fake server, with the interface services the app registers.
/// </summary>
public abstract class ScreenTest : BunitContext
{
    #region Constructors

    protected ScreenTest()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
        Services.AddSingleton(Server.CreateApi());
        Services.AddSingleton(new UiEnvironment(IsDevelopment: false));
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

    protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    protected string Address => Navigation.ToBaseRelativePath(Navigation.Uri);

    #endregion

    #region Public Methods

    protected void SignedIn() => Server.Answer("GET", "/api/session", Person);

    protected void SignedOut(bool setupOpen)
    {
        Server.Answer("GET", "/api/session", HttpStatusCode.Unauthorized);
        if (setupOpen)
        {
            Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: true));
        }
    }

    #endregion
}
