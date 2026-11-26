namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The request to remove the retired Drover Preview app: shown only while it is installed, sending the person to its
/// system page when they agree, and asked again at the next start when they do not.
/// </summary>
public sealed class RetiredAppViewModelTests
{
    #region Tests

    [Fact]
    public void The_request_opens_when_the_retired_app_is_installed()
    {
        var model = new RetiredAppViewModel(new FakeRetiredApp { Installed = true });

        model.Check();

        model.Open.Should().BeTrue();
    }

    [Fact]
    public void Nothing_is_asked_when_the_retired_app_is_gone()
    {
        var model = new RetiredAppViewModel(new FakeRetiredApp { Installed = false });

        model.Check();

        model.Open.Should().BeFalse();
    }

    [Fact]
    public void Nothing_is_asked_where_the_platform_has_no_retired_app()
    {
        var model = new RetiredAppViewModel();

        model.Check();

        model.Open.Should().BeFalse();
    }

    [Fact]
    public void Uninstalling_opens_the_retired_apps_system_page_and_closes_the_request()
    {
        var retired = new FakeRetiredApp { Installed = true };
        var model = new RetiredAppViewModel(retired);
        model.Check();

        model.Remove();

        retired.SettingsOpened.Should().Be(1);
        model.Open.Should().BeFalse();
    }

    [Fact]
    public void Not_now_closes_the_request_until_the_next_start()
    {
        var retired = new FakeRetiredApp { Installed = true };
        var model = new RetiredAppViewModel(retired);
        model.Check();

        model.Later();

        model.Open.Should().BeFalse();
        retired.SettingsOpened.Should().Be(0);

        model.Check();

        model.Open.Should().BeTrue();
    }

    [Fact]
    public void The_request_names_the_app_as_it_shows_under_its_icon()
    {
        RetiredAppViewModel.Sentence.Should().StartWith("Drover Preview is the old app.");
        RetiredAppViewModel.RemoveLabel.Should().Be("Uninstall Drover Preview");
    }

    #endregion

    #region Fakes

    private sealed class FakeRetiredApp : IRetiredApp
    {
        #region Properties

        public int SettingsOpened { get; private set; }

        #endregion

        #region Interface Implementations

        public bool Installed { get; init; }

        public void OpenSettings() => SettingsOpened++;

        #endregion
    }

    #endregion
}
