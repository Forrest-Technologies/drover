namespace Drover.Presentation.Tests.ViewModels;

/// <summary>Cross-device appearance loading, local application, and retry behavior.</summary>
public sealed class AppearanceViewModelTests : ViewModelTest
{
    #region Private Fields

    private const string Path = "/api/account/appearance";

    private readonly MemorySettings settings = new();

    private readonly ThemeModel theme;

    private readonly AppearanceViewModel model;

    #endregion

    #region Constructors

    public AppearanceViewModelTests()
    {
        theme = new(settings, NullLogger<ThemeModel>.Instance);
        theme.Load();
        model = new(
            theme,
            Navigator,
            Server.CreateApi(),
            Session,
            NullLogger<AppearanceViewModel>.Instance);
    }

    #endregion

    #region Public Methods

    public new void Dispose()
    {
        model.Dispose();
        base.Dispose();
    }

    [Fact]
    public async Task NoSavedChoice_UsesDefaultTheme()
    {
        Server.Answer("GET", Path, HttpStatusCode.NotFound);

        Session.Begin(Person);
        await Eventually(() => Server.Requests.Any(request => request.Method == "GET" && request.Path == Path));

        theme.Choice.Should().Be(new ThemeChoice("front-office", ThemeMode.Dark, "theme"));
        Server.Requests.Should().NotContain(request => request.Method == "PUT" && request.Path == Path);
    }

    [Fact]
    public async Task Offline_uses_last_applied_choice_and_saves_on_reconnect()
    {
        Server.Answer("GET", Path, HttpStatusCode.NotFound);
        Server.Answer("PUT", Path, HttpStatusCode.NoContent);
        Session.Begin(Person);
        await Eventually(() => Server.Requests.Any(request => request.Method == "GET" && request.Path == Path));
        await Session.OpenScreen(
            () => Task.FromException(new HttpRequestException("offline")),
            NullLogger.Instance);

        model.ChooseTheme(ThemeMode.Black);
        model.ChooseAccent("green");

        theme.Choice.Should().Be(new ThemeChoice("front-office", ThemeMode.Black, "green"));
        Server.Requests.Should().NotContain(request => request.Method == "PUT" && request.Path == Path);

        await Session.OpenScreen(() => Task.CompletedTask, NullLogger.Instance);
        await Eventually(() => Server.Requests.Count(request => request.Method == "PUT" && request.Path == Path) == 1);

        Server.Sent("PUT", Path).Single().Should()
            .Be("""{"themeId":"front-office","mode":"black","accent":"green"}""");
    }

    [Fact]
    public void SignedOut_uses_device_setting_without_calling_server()
    {
        settings.Values[ThemeModel.SettingName] = new ThemeChoice("night-yard", ThemeMode.Light, "theme").ToStored();
        theme.Load();

        model.ChooseTheme(ThemeMode.Black);
        model.ChooseAccent("green");

        theme.Choice.Should().Be(new ThemeChoice("night-yard", ThemeMode.Black, "green"));
        ThemeChoice.Parse(settings.Values[ThemeModel.SettingName], NullLogger.Instance).Should().Be(theme.Choice);
        Server.Requests.Should().NotContain(request => request.Path == Path);
    }

    [Fact]
    public async Task SaveFailure_keeps_local_choice_and_shows_retry_note()
    {
        Server.Answer("GET", Path, HttpStatusCode.NotFound);
        Server.Answer("PUT", Path, HttpStatusCode.InternalServerError);
        Session.Begin(Person);
        await Eventually(() => Server.Requests.Any(request => request.Method == "GET" && request.Path == Path));

        model.ChooseTheme(ThemeMode.Black);
        model.ChooseAccent("blue");
        await Eventually(() => model.SyncNote is not null);

        theme.Choice.Should().Be(new ThemeChoice("front-office", ThemeMode.Black, "blue"));
        model.SyncNote.Should().Be("Saved on this device. Drover could not save it to your account.");
        model.RetrySyncCommand.Should().NotBeNull();
    }

    #endregion
}
