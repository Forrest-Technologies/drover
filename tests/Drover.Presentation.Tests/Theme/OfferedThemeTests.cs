namespace Drover.Presentation.Tests.Theme;

/// <summary>
/// Themes a space's extensions offer: each is checked on the device before it can be chosen, the one in use is kept
/// on the device so the app starts in it, and a theme the space no longer offers is no longer drawn. Every value is
/// a sample.
/// </summary>
public sealed class OfferedThemeTests : ViewModelTest, IDisposable
{
    #region Private Fields

    private static readonly string themes = $"/api/spaces/{SpaceId}/themes";

    private readonly MemorySettings settings = new();

    private readonly ThemeModel theme;

    private readonly AppearanceViewModel model;

    #endregion

    #region Constructors

    public OfferedThemeTests()
    {
        theme = new(settings, NullLogger<ThemeModel>.Instance);
        theme.Load();
        model = new(theme, Navigator, Server.CreateApi(), Session, NullLogger<AppearanceViewModel>.Instance);
    }

    #endregion

    #region Tests

    void IDisposable.Dispose()
    {
        model.Dispose();
        base.Dispose();
    }

    [Fact]
    public void A_theme_that_passes_the_checks_is_offered_chosen_and_kept_on_the_device()
    {
        theme.Offer([new SpaceTheme("sample-dusk", "Sample dusk", "Sample studio", Sample("sample-dusk"))]);

        model.ChooseLook("sample-dusk");

        model.OfferedLooks.Should().ContainSingle().Which.Should().Match<LookOption>(
            look => look.Id == "sample-dusk" && look.Name == "Sample dusk" && look.Source == "Sample studio" && look.InUse);
        AppearanceViewModel.OfferedLegend.Should().Be("From this space's extensions");
        model.Looks.Should().NotContain(look => look.InUse, "the theme in use is the offered one, not one of the app's own");
        theme.Chosen.Id.Should().Be("sample-dusk");
        settings.Values[ThemeModel.KeptThemeName].Should().Be(Sample("sample-dusk"), "the next start draws it before the server answers");
    }

    [Fact]
    public void A_theme_that_fails_a_check_or_takes_one_of_the_apps_own_ids_is_not_offered()
    {
        var unreadable = System.Text.Json.Nodes.JsonNode.Parse(Sample("sample-fog"))!.AsObject();
        unreadable["dark"]!["color"]!["text"] = unreadable["dark"]!["color"]!["bg-0"]!.GetValue<string>();

        theme.Offer(
        [
            new SpaceTheme("sample-fog", "Sample fog", "Sample studio", unreadable.ToJsonString()),
            new SpaceTheme("night-yard", "Sample impostor", "Sample studio", Sample("night-yard")),
            new SpaceTheme("sample-other", "Sample mismatch", "Sample studio", Sample("sample-dusk")),
            new SpaceTheme("sample-broken", "Sample broken", "Sample studio", "not a theme"),
        ]);
        theme.ChooseTheme("sample-fog");

        theme.Offered.Should().BeEmpty("the server checked each, and the device does not take that on trust");
        theme.Chosen.Should().BeSameAs(BuiltInThemes.Default, "a theme that is not offered is not drawn");
        BuiltInThemes.Get("night-yard")!.Name.Should().NotBe("Sample impostor");
        settings.Values.Should().NotContainKey(ThemeModel.KeptThemeName);
    }

    [Fact]
    public void The_app_starts_in_the_kept_theme_and_goes_back_to_the_default_once_the_space_no_longer_offers_it()
    {
        theme.Offer([new SpaceTheme("sample-dusk", "Sample dusk", "Sample studio", Sample("sample-dusk"))]);
        theme.ChooseTheme("sample-dusk");
        var restarted = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);

        restarted.Load();
        var atStart = restarted.Chosen.Id;
        restarted.Offer([]);

        atStart.Should().Be("sample-dusk", "the copy on the device is checked and drawn before the server has answered");
        restarted.Chosen.Should().BeSameAs(BuiltInThemes.Default, "the extension was removed, so its theme is no longer drawn");
        restarted.Choice.ThemeId.Should().Be("sample-dusk", "the choice is the person's and is left as it is");
        settings.Values.Should().NotContainKey(ThemeModel.KeptThemeName);
    }

    [Fact]
    public void A_theme_offered_again_with_the_same_file_is_the_same_theme_so_the_look_in_use_is_not_put_in_place_again()
    {
        SpaceTheme[] offered = [new("sample-dusk", "Sample dusk", "Sample studio", Sample("sample-dusk"))];
        theme.Offer(offered);
        theme.ChooseTheme("sample-dusk");
        var first = theme.Theme;
        var restarted = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);
        restarted.Load();
        var atStart = restarted.Theme;

        theme.Offer(offered);
        restarted.Offer(offered);

        theme.Theme.Should().BeSameAs(first, "the shell puts a look in place again only when the theme is another object");
        restarted.Theme.Should().BeSameAs(atStart, "the copy kept on the device is the file the space still offers");
    }

    [Fact]
    public void A_kept_theme_that_no_longer_passes_the_checks_is_not_drawn()
    {
        settings.Values[ThemeModel.SettingName] = new ThemeChoice("sample-dusk", ThemeMode.Dark).ToStored();
        settings.Values[ThemeModel.KeptThemeName] = "not a theme";
        var started = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);

        started.Load();

        started.Chosen.Should().BeSameAs(BuiltInThemes.Default);
    }

    [Fact]
    public async Task What_the_space_offers_is_read_before_the_account_choice_so_a_choice_of_an_offered_theme_is_found()
    {
        Server.Answer("GET", themes, (SpaceTheme[])[new("sample-dusk", "Sample dusk", "Sample studio", Sample("sample-dusk"))]);
        Server.Answer("GET", "/api/account/appearance", new AppearanceSetting("sample-dusk", "light"));

        Session.Begin(Person);
        await Eventually(() => theme.Choice.ThemeId == "sample-dusk");

        theme.Chosen.Id.Should().Be("sample-dusk");
        theme.Choice.Mode.Should().Be(ThemeMode.Light);
        Server.Requests.Should().NotContain(request => request.Method == "PUT", "applying the account's choice sends nothing back");
    }

    [Fact]
    public async Task Opening_the_choices_reads_what_the_space_offers_again()
    {
        Server.Answer("GET", themes, Array.Empty<SpaceTheme>());
        Server.Answer("GET", "/api/account/appearance", HttpStatusCode.NotFound);
        Session.Begin(Person);
        await Eventually(() => Server.Requests.Any(request => request.Path == "/api/account/appearance"));
        Server.Answer("GET", themes, (SpaceTheme[])[new("sample-dusk", "Sample dusk", "Sample studio", Sample("sample-dusk"))]);

        theme.AppearanceOpen = true;
        await Eventually(() => model.OfferedLooks.Count == 1);

        model.OfferedLooks.Single().Name.Should().Be("Sample dusk", "an extension imported since is offered when the choices open");
    }

    [Fact]
    public void Every_theme_the_app_has_keeps_an_id_no_extension_may_take() =>
        ThemeIds.Reserved.Should().Contain(BuiltInThemes.All.Select(built => built.Id))
            .And.Contain(Directory.GetFiles(Repository.ThemeFolder, "*.json").Select(file => System.IO.Path.GetFileNameWithoutExtension(file)));

    #endregion

    #region Helpers

    /// <summary>Night Yard's file under another id, so it passes every theme check.</summary>
    private static string Sample(string id)
    {
        var file = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(Repository.ThemeFolder, "night-yard.json")))!.AsObject();
        file["id"] = id;
        file["name"] = "Sample dusk";
        return file.ToJsonString();
    }

    #endregion
}
