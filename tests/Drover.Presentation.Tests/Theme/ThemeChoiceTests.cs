namespace Drover.Presentation.Tests.Theme;

/// <summary>
/// The theme choice a device keeps: a theme and a mode, with the one-word value an older version wrote.
/// </summary>
public sealed class ThemeChoiceTests
{
    #region Public Methods

    [Theory]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("system", ThemeMode.System)]
    [InlineData("Light", ThemeMode.Light)]
    public void An_old_one_word_value_reads_as_night_yard_in_that_mode(string stored, ThemeMode mode) =>
        ThemeChoice.Parse(stored, NullLogger.Instance).Should().Be(new ThemeChoice("night-yard", mode));

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
    public void A_choice_is_kept_and_read_back(ThemeMode mode)
    {
        var choice = new ThemeChoice("night-yard", mode);

        ThemeChoice.Parse(choice.ToStored(), NullLogger.Instance).Should().Be(choice);
    }

    [Fact]
    public void A_choice_is_kept_as_a_theme_id_and_a_mode() =>
        new ThemeChoice("night-yard", ThemeMode.Light).ToStored()
            .Should().Be("""{"themeId":"night-yard","mode":"light"}""");

    [Fact]
    public void The_model_reads_an_old_value_and_keeps_the_next_choice_in_the_new_form()
    {
        var settings = new MemorySettings();
        settings.Values[ThemeModel.SettingName] = "light";
        var model = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);

        model.Load();
        model.Choice.Should().Be(new ThemeChoice("night-yard", ThemeMode.Light));
        model.Theme.Id.Should().Be("night-yard");

        model.Choose(ThemeMode.System);
        settings.Values[ThemeModel.SettingName].Should().Be("""{"themeId":"night-yard","mode":"system"}""");
    }

    [Fact]
    public void A_theme_the_app_does_not_have_draws_as_night_yard_but_keeps_the_name_it_was_given()
    {
        var settings = new MemorySettings();
        settings.Values[ThemeModel.SettingName] = new ThemeChoice("signal-room", ThemeMode.Dark).ToStored();
        var model = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);

        model.Load();

        model.Choice.ThemeId.Should().Be("signal-room");
        model.Theme.Id.Should().Be("night-yard");
    }

    #endregion
}
