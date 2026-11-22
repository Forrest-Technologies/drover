namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The Appearance page: a theme applies at once, the one before is offered until the person leaves, the mode is its
/// own choice, and a device uses the theme kept for all the person's devices only when they ask it to.
/// </summary>
public sealed class AppearanceViewModelTests : ViewModelTest
{
    #region Private Fields

    private const string Path = "/api/account/appearance";

    private readonly MemorySettings settings = new();

    private readonly ThemeModel theme;

    private readonly ThemeSync sync;

    private readonly AppearanceTrail trail;

    private readonly AppearanceViewModel model;

    #endregion

    #region Constructors

    public AppearanceViewModelTests()
    {
        BuiltInThemes.AddSample(Sample);
        theme = new(settings, NullLogger<ThemeModel>.Instance);
        sync = new(theme, Session, Server.CreateApi(), settings, NullLogger<ThemeSync>.Instance);
        trail = new(Navigator);
        model = new(theme, sync, trail, NullLogger<AppearanceViewModel>.Instance);
        Navigator.NavigateTo(Addresses.Appearance);
    }

    #endregion

    #region Properties

    /// <summary>A theme known only to these tests, marked as a sample, so a choice can move between two.</summary>
    private static ThemeFile Sample { get; } = BuiltInThemes.NightYard with { Id = "sample-test-theme", Name = "Sample test theme" };

    #endregion

    #region Public Methods

    [Fact]
    public void Every_theme_the_app_has_is_listed_in_its_group_with_its_name_and_intent_as_its_accessible_name()
    {
        var group = model.Groups.Should().ContainSingle().Subject;

        group.Heading.Should().Be("Expressive");
        var option = group.Themes.Should().ContainSingle().Subject;
        option.Theme.Id.Should().Be("night-yard");
        option.InUse.Should().BeTrue();
        option.AccessibleName.Should().Be("Night Yard, A dark, calm workspace where the bots are the lit things.");
    }

    [Fact]
    public void Choosing_a_theme_applies_it_at_once_keeps_it_on_the_device_and_offers_the_one_before()
    {
        model.BackLabel.Should().BeNull("nothing was chosen yet");

        model.ChooseTheme(Sample.Id);

        theme.Theme.Id.Should().Be(Sample.Id);
        ThemeChoice.Parse(settings.Values[ThemeModel.SettingName], NullLogger.Instance).Should().Be(new ThemeChoice(Sample.Id, ThemeMode.Dark));
        model.BackLabel.Should().Be("Back to Night Yard, dark");
    }

    [Fact]
    public void Back_returns_to_the_theme_the_person_came_with_even_after_several_choices()
    {
        model.ChooseMode(nameof(ThemeMode.Light));
        model.ChooseTheme(Sample.Id);

        model.GoBack();

        theme.Choice.Should().Be(new ThemeChoice("night-yard", ThemeMode.Dark));
        model.BackLabel.Should().BeNull("the theme in use is the one before");
    }

    [Fact]
    public void The_way_back_is_kept_while_the_page_is_built_again_and_forgotten_when_the_person_leaves()
    {
        model.ChooseTheme(Sample.Id);

        new AppearanceViewModel(theme, sync, trail, NullLogger<AppearanceViewModel>.Instance)
            .BackLabel.Should().Be("Back to Night Yard, dark");

        Navigator.NavigateTo(Addresses.Bots);
        Navigator.NavigateTo(Addresses.Appearance);

        model.BackLabel.Should().BeNull();
    }

    [Fact]
    public void The_mode_is_its_own_choice_and_follows_the_device_when_asked()
    {
        model.Modes.Select(option => option.Label).Should().Equal("Follow device", "Dark", "Light");

        model.ChooseMode(nameof(ThemeMode.System));

        theme.Choice.Mode.Should().Be(ThemeMode.System);
        model.Mode.Should().Be(nameof(ThemeMode.System));
        model.BackLabel.Should().Be("Back to Night Yard, dark");
    }

    [Fact]
    public void A_device_keeps_its_own_theme_until_the_person_asks_for_one_theme_everywhere()
    {
        model.ChooseTheme(Sample.Id);

        model.UsesEverywhere.Should().BeFalse();
        Server.Requests.Should().NotContain(request => request.Path == Path);
    }

    [Fact]
    public async Task One_theme_everywhere_keeps_the_theme_in_use_for_the_person_and_follows_it_from_then_on()
    {
        SignedIn();
        await Session.Load(TestContext.Current.CancellationToken);
        Server.Answer("PUT", Path, HttpStatusCode.NoContent);

        await model.SetEverywhere(true);
        model.ChooseMode(nameof(ThemeMode.Light));

        model.UsesEverywhere.Should().BeTrue();
        settings.Values[ThemeSync.SettingName].Should().Be("on");
        await Eventually(() => Server.Requests.Count(request => request.Method == "PUT" && request.Path == Path) == 2);
        Server.Requests.Where(request => request.Path == Path).Select(request => request.Body)
            .Should().Equal("""{"themeId":"night-yard","mode":"dark"}""", """{"themeId":"night-yard","mode":"light"}""");
    }

    [Fact]
    public async Task When_the_server_cannot_keep_it_the_page_says_so_and_the_device_keeps_its_own()
    {
        SignedIn();
        await Session.Load(TestContext.Current.CancellationToken);
        Server.Answer("PUT", Path, HttpStatusCode.InternalServerError);

        await model.SetEverywhere(true);

        model.UsesEverywhere.Should().BeFalse();
        model.Problem.Should().StartWith("Your theme was not kept for your other devices.");
        settings.Values.Should().NotContainKey(ThemeSync.SettingName);
    }

    [Fact]
    public async Task A_device_that_follows_takes_the_kept_theme_when_the_person_signs_in_and_one_that_does_not_never_asks()
    {
        Server.Answer("GET", Path, new AppearanceSetting(Sample.Id, "light"));
        SignedIn();
        await Session.Load(TestContext.Current.CancellationToken);
        await sync.TakeKept(TestContext.Current.CancellationToken);

        theme.Choice.Should().Be(ThemeChoice.Default);
        Server.Requests.Should().NotContain(request => request.Path == Path);

        settings.Values[ThemeSync.SettingName] = "on";
        var following = new ThemeSync(theme, Session, Server.CreateApi(), settings, NullLogger<ThemeSync>.Instance);
        await following.TakeKept(TestContext.Current.CancellationToken);

        theme.Choice.Should().Be(new ThemeChoice(Sample.Id, ThemeMode.Light));
        Server.Requests.Should().NotContain(request => request.Method == "PUT", "a theme taken from the account is not sent back");
    }

    [Fact]
    public void Turning_it_off_keeps_the_theme_on_this_device_and_stops_following()
    {
        settings.Values[ThemeSync.SettingName] = "on";
        var following = new ThemeSync(theme, Session, Server.CreateApi(), settings, NullLogger<ThemeSync>.Instance);
        var page = new AppearanceViewModel(theme, following, trail, NullLogger<AppearanceViewModel>.Instance);

        _ = page.SetEverywhere(false);

        page.UsesEverywhere.Should().BeFalse();
        settings.Values[ThemeSync.SettingName].Should().Be("off");
    }

    [Fact]
    public void A_device_that_asks_for_less_motion_or_larger_text_is_told_which_themes_suit_it_and_nothing_is_hidden()
    {
        model.DeviceAsks([DeviceRequest.ReducedMotion]);

        model.Suggestions.Should().BeEmpty("no theme the app has yet is made for less motion");
        model.Groups.SelectMany(group => group.Themes).Should().NotBeEmpty();
    }

    #endregion
}
