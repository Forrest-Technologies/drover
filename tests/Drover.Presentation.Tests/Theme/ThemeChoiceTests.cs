namespace Drover.Presentation.Tests.Theme;

/// <summary>
/// The theme choice a device keeps: a theme, a mode and an accent, with the values older versions wrote.
/// </summary>
public sealed class ThemeChoiceTests
{
    #region Public Methods

    [Theory]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("system", ThemeMode.System)]
    [InlineData("Light", ThemeMode.Light)]
    public void An_old_one_word_value_reads_as_the_default_theme_in_that_mode(string stored, ThemeMode mode) =>
        ThemeChoice.Parse(stored, NullLogger.Instance).Should().Be(new ThemeChoice("front-office", mode));

    [Fact]
    public void An_object_written_before_a_theme_could_be_chosen_keeps_its_mode_and_takes_the_default_theme() =>
        ThemeChoice.Parse("""{"themeId":"night-yard","mode":"light"}""", NullLogger.Instance)
            .Should().Be(new ThemeChoice("front-office", ThemeMode.Light));

    [Fact]
    public void A_stored_choice_with_no_accent_or_an_empty_one_has_the_themes_own_color()
    {
        ThemeChoice.Parse("""{"themeId":"night-yard","mode":"dark","version":2}""", NullLogger.Instance)
            .Should().Be(new ThemeChoice("night-yard", ThemeMode.Dark, Accents.ThemeId));
        ThemeChoice.Parse("""{"themeId":"night-yard","mode":"dark","accent":"","version":2}""", NullLogger.Instance)
            .Accent.Should().Be(Accents.ThemeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("blue")]
    [InlineData("7")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("""{"themeId":"signal-room"}""")]
    [InlineData("""{"themeId":"","mode":"dark"}""")]
    [InlineData("""{"themeId":"signal-room","mode":"purple"}""")]
    [InlineData("[]")]
    public void A_value_that_is_nothing_usable_is_the_default_and_never_an_error(string? stored) =>
        ThemeChoice.Parse(stored, NullLogger.Instance).Should().Be(ThemeChoice.Default);

    [Theory]
    [InlineData(ThemeMode.Dark)]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.System)]
    [InlineData(ThemeMode.Black)]
    public void A_choice_is_kept_and_read_back(ThemeMode mode)
    {
        var choice = new ThemeChoice("night-yard", mode, "green");

        ThemeChoice.Parse(choice.ToStored(), NullLogger.Instance).Should().Be(choice);
    }

    [Fact]
    public void A_choice_is_kept_as_a_theme_id_a_mode_an_accent_and_the_version_that_makes_the_theme_a_choice() =>
        new ThemeChoice("night-yard", ThemeMode.Black, "green").ToStored()
            .Should().Be("""{"themeId":"night-yard","mode":"black","accent":"green","version":2}""");

    [Fact]
    public void The_model_reads_an_old_value_and_keeps_the_next_choice_in_the_new_form()
    {
        var settings = new MemorySettings();
        settings.Values[ThemeModel.SettingName] = "light";
        var model = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);

        model.Load();
        model.Choice.Should().Be(new ThemeChoice("front-office", ThemeMode.Light));
        model.Theme.Id.Should().Be("front-office");

        model.Choose(ThemeMode.System);
        settings.Values[ThemeModel.SettingName].Should()
            .Be("""{"themeId":"front-office","mode":"system","accent":"theme","version":2}""");
    }

    [Fact]
    public void A_theme_the_app_does_not_have_draws_as_the_default_but_keeps_the_name_it_was_given()
    {
        var settings = new MemorySettings();
        settings.Values[ThemeModel.SettingName] = new ThemeChoice("signal-room", ThemeMode.Dark).ToStored();
        var model = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);

        model.Load();

        model.Choice.ThemeId.Should().Be("signal-room");
        model.Theme.Id.Should().Be("front-office");
    }

    [Fact]
    public void Choosing_a_theme_keeps_the_mode_and_returns_to_the_themes_own_color()
    {
        var model = new ThemeModel(new MemorySettings(), NullLogger<ThemeModel>.Instance);
        model.Choose(new ThemeChoice("front-office", ThemeMode.Black, "blue"));

        model.ChooseTheme("night-yard");

        model.Choice.Should().Be(new ThemeChoice("night-yard", ThemeMode.Black));
        model.IsBlack.Should().BeTrue();
        model.Theme.Should().BeSameAs(BuiltInThemes.NightYard);
    }

    [Fact]
    public void An_accent_the_theme_does_not_offer_draws_in_the_themes_own_color()
    {
        var model = new ThemeModel(new MemorySettings(), NullLogger<ThemeModel>.Instance);

        model.Choose(new ThemeChoice("front-office", ThemeMode.Dark, "no-such-accent"));

        model.Theme.Should().BeSameAs(BuiltInThemes.Default);
    }

    #endregion
}
