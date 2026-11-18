namespace Drover.Presentation.Tests.Routing;

/// <summary>Where the app is, and the addresses it came through.</summary>
public sealed class NavigatorTests
{
    #region Private Fields

    private readonly Navigator navigator = new();

    private readonly List<(string Address, NavigationKind Kind)> heard = [];

    #endregion

    #region Constructors

    public NavigatorTests()
    {
        navigator.Navigated += (address, kind) => heard.Add((address, kind));
    }

    #endregion

    #region Public Methods

    [Fact]
    public void It_starts_at_the_start_address_with_nowhere_to_go_back_to()
    {
        navigator.Current.Should().Be(Addresses.Home);
        navigator.CanGoBack.Should().BeFalse();
        navigator.GoBack().Should().BeFalse();
        heard.Should().BeEmpty();
    }

    [Fact]
    public void Going_somewhere_adds_a_step_that_can_be_gone_back_from()
    {
        navigator.NavigateTo(Addresses.Bots);
        navigator.NavigateTo(Addresses.Tools);

        navigator.GoBack().Should().BeTrue();

        navigator.Current.Should().Be(Addresses.Bots);
        navigator.CanGoForward.Should().BeTrue();
        heard.Should().Equal(
            (Addresses.Bots, NavigationKind.Push), (Addresses.Tools, NavigationKind.Push), (Addresses.Bots, NavigationKind.Travel));
    }

    [Fact]
    public void Replacing_leaves_no_step_behind()
    {
        navigator.NavigateTo(Addresses.Bots);
        navigator.NavigateTo(Addresses.SignIn, replace: true);

        navigator.GoBack().Should().BeTrue();

        navigator.Current.Should().Be(Addresses.Home);
        heard[1].Should().Be((Addresses.SignIn, NavigationKind.Replace));
    }

    [Fact]
    public void Going_somewhere_new_after_going_back_drops_what_was_ahead()
    {
        navigator.NavigateTo(Addresses.Bots);
        navigator.NavigateTo(Addresses.Tools);
        navigator.GoBack();

        navigator.NavigateTo(Addresses.Models);

        navigator.CanGoForward.Should().BeFalse();
        navigator.GoBack();
        navigator.Current.Should().Be(Addresses.Bots);
    }

    [Fact]
    public void The_address_the_app_was_opened_at_becomes_the_only_entry()
    {
        navigator.StartAt(Addresses.Usage);

        navigator.Current.Should().Be(Addresses.Usage);
        navigator.CanGoBack.Should().BeFalse();
        heard.Should().BeEmpty();
    }

    [Fact]
    public void When_the_browser_steps_back_or_forward_the_app_follows_through_its_own_history()
    {
        navigator.NavigateTo(Addresses.Bots);
        navigator.NavigateTo(Addresses.Tools);

        navigator.Arrive(Addresses.Bots);
        navigator.CanGoForward.Should().BeTrue();
        navigator.Arrive(Addresses.Tools);

        navigator.Current.Should().Be(Addresses.Tools);
        navigator.CanGoForward.Should().BeFalse();
        heard.TakeLast(2).Should().Equal((Addresses.Bots, NavigationKind.Travel), (Addresses.Tools, NavigationKind.Travel));
    }

    [Fact]
    public void An_address_the_browser_arrives_at_from_outside_the_apps_history_is_taken_as_where_it_is()
    {
        navigator.NavigateTo(Addresses.Bots);

        navigator.Arrive(Addresses.Status);

        navigator.Current.Should().Be(Addresses.Status);
        navigator.GoBack();
        navigator.Current.Should().Be(Addresses.Home);
    }

    #endregion
}
