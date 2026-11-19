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

    [Fact]
    public void Browser_back_to_the_entry_before_asks_the_screen_first_and_goes_when_it_agrees()
    {
        navigator.NavigateTo(Addresses.Bots);
        navigator.NavigateTo(Addresses.Tools);
        string? asked = null;
        navigator.Leaving = target =>
        {
            asked = target;
            return true;
        };

        navigator.Arrive(Addresses.Bots).Should().BeTrue();

        asked.Should().Be(Addresses.Bots);
        navigator.Current.Should().Be(Addresses.Bots);
        navigator.Next.Should().Be(Addresses.Tools);
    }

    [Fact]
    public void Browser_back_from_a_screen_holding_work_stays_so_the_address_is_put_back()
    {
        navigator.NavigateTo(Addresses.Bots);
        navigator.NavigateTo(Addresses.Tools);
        navigator.Leaving = _ => false;

        navigator.Arrive(Addresses.Bots).Should().BeFalse();

        navigator.Current.Should().Be(Addresses.Tools);
        navigator.Previous.Should().Be(Addresses.Bots);
        heard.Should().HaveCount(2, "staying does not move the app");
    }

    [Fact]
    public void Browser_forward_or_an_address_from_outside_the_history_from_a_screen_holding_work_stays()
    {
        navigator.NavigateTo(Addresses.Bots);
        navigator.NavigateTo(Addresses.Tools);
        navigator.GoBack();
        List<string> asked = [];
        navigator.Leaving = target =>
        {
            asked.Add(target);
            return false;
        };

        navigator.Arrive(Addresses.Tools).Should().BeFalse();
        navigator.Arrive(Addresses.Status).Should().BeFalse();

        asked.Should().Equal(Addresses.Tools, Addresses.Status);
        navigator.Current.Should().Be(Addresses.Bots);
        navigator.Next.Should().Be(Addresses.Tools);
    }

    [Fact]
    public void Browser_back_with_something_open_closes_it_and_stays()
    {
        navigator.NavigateTo(Addresses.Bots);
        var closed = false;
        navigator.AddCloser(() => closed = true);

        navigator.Arrive(Addresses.Home).Should().BeFalse();

        closed.Should().BeTrue();
        navigator.Current.Should().Be(Addresses.Bots);
    }

    [Fact]
    public void Arriving_where_the_app_already_is_changes_nothing_and_asks_nothing()
    {
        navigator.NavigateTo(Addresses.Bots);
        navigator.Leaving = _ => throw new InvalidOperationException("Nothing should be asked.");

        navigator.Arrive(Addresses.Bots).Should().BeTrue();

        heard.Should().ContainSingle();
    }

    [Fact]
    public void A_step_back_closes_what_was_opened_last_and_stays_on_the_screen()
    {
        navigator.NavigateTo(Addresses.Bots);
        List<string> closed = [];
        Action rail = () => closed.Add("rail");
        Action dialog = () => closed.Add("dialog");
        navigator.AddCloser(rail);
        navigator.AddCloser(dialog);

        navigator.StepBack().Should().Be(BackStep.Closed);
        navigator.StepBack().Should().Be(BackStep.Closed);

        closed.Should().Equal("dialog", "rail");
        navigator.Current.Should().Be(Addresses.Bots);
        heard.Should().ContainSingle("closing does not move the app");
    }

    [Fact]
    public void A_step_back_with_nothing_open_goes_to_the_address_before()
    {
        navigator.NavigateTo(Addresses.Bots);
        Action closed = () => { };
        navigator.AddCloser(closed);
        navigator.RemoveCloser(closed);

        navigator.StepBack().Should().Be(BackStep.WentBack);

        navigator.Current.Should().Be(Addresses.Home);
    }

    [Fact]
    public void A_step_back_from_a_screen_holding_work_stays_and_lets_it_ask_first()
    {
        navigator.NavigateTo(Addresses.Bots);
        string? asked = null;
        navigator.Leaving = target =>
        {
            asked = target;
            return false;
        };

        navigator.StepBack().Should().Be(BackStep.Held);

        asked.Should().Be(Addresses.Home);
        navigator.Current.Should().Be(Addresses.Bots);
    }

    [Fact]
    public void A_step_back_from_the_first_screen_with_nothing_open_leaves_the_app()
    {
        navigator.CanStepBack.Should().BeFalse();

        navigator.StepBack().Should().Be(BackStep.Out);

        navigator.Current.Should().Be(Addresses.Home);
        heard.Should().BeEmpty();
    }

    [Fact]
    public void Whether_a_step_back_stays_in_the_app_is_announced_as_it_changes()
    {
        var announced = 0;
        navigator.StepBackChanged += () => announced++;
        Action dialog = () => { };

        navigator.AddCloser(dialog);
        navigator.CanStepBack.Should().BeTrue("an open dialog closes first");
        navigator.AddCloser(dialog);
        navigator.RemoveCloser(dialog);
        navigator.CanStepBack.Should().BeFalse();
        navigator.NavigateTo(Addresses.Bots);
        navigator.CanStepBack.Should().BeTrue();

        announced.Should().Be(3, "adding the same closer twice changes nothing");
    }

    [Fact]
    public void Returning_to_the_address_before_goes_back_and_to_another_takes_its_place()
    {
        navigator.NavigateTo(Addresses.Bots);
        navigator.NavigateTo(Addresses.Tools);

        navigator.ReturnTo(Addresses.Bots);
        navigator.Current.Should().Be(Addresses.Bots);
        navigator.CanGoForward.Should().BeTrue("it went back");

        navigator.ReturnTo(Addresses.Models);
        navigator.Current.Should().Be(Addresses.Models);
        navigator.Previous.Should().Be(Addresses.Home, "the address left leaves no step behind");
    }

    #endregion
}
