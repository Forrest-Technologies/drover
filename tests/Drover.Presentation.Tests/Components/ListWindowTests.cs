namespace Drover.Presentation.Tests.Components;

/// <summary>
/// The page over a long list: the newest items first, more as the reader goes back or comes forward, one item
/// landed on wherever it is, never more than the page keeps, and never a group cut in two. The list is never cut.
/// </summary>
public sealed class ListWindowTests
{
    #region Public Methods

    [Fact]
    public void A_short_list_is_shown_whole_as_the_list_itself()
    {
        var window = Window();
        var items = Items(8);

        window.Show(items);

        window.Shown.Should().BeSameAs(items);
        (window.Earlier, window.Later).Should().Be((0, 0));
    }

    [Fact]
    public void A_long_list_shows_its_newest_page()
    {
        var window = Window();

        window.Show(Items(100));

        window.Shown.Should().Equal(Items(100).Skip(90));
        (window.Earlier, window.Later).Should().Be((90, 0));
    }

    [Fact]
    public void An_empty_list_shows_nothing_and_then_what_arrives()
    {
        var window = Window();

        window.Show([]);
        window.Shown.Should().BeEmpty();

        window.Show(Items(3));
        window.Shown.Should().Equal(Items(3));
    }

    [Fact]
    public void The_page_follows_the_end_and_keeps_its_start_while_items_arrive()
    {
        var window = Window();
        window.Show(Items(100));

        window.Show(Items(103));

        window.Shown.Should().Equal(Items(103).Skip(90));
        window.Later.Should().Be(0);
    }

    [Fact]
    public void A_page_that_follows_the_end_lets_go_of_its_start_past_the_most_it_keeps()
    {
        var window = Window();
        window.Show(Items(100));

        window.Show(Items(140));

        window.Shown.Should().Equal(Items(140).Skip(110));
        window.Later.Should().Be(0);
    }

    [Fact]
    public void Going_back_shows_a_step_more_until_nothing_comes_before()
    {
        var window = Window();
        window.Show(Items(18));

        window.ShowEarlier().Should().BeTrue();
        window.Shown.Should().Equal(Items(18).Skip(3));

        window.ShowEarlier().Should().BeTrue();
        window.Shown.Should().Equal(Items(18));
        window.ShowEarlier().Should().BeFalse();
    }

    [Fact]
    public void Going_far_back_lets_go_of_the_end_and_the_page_no_longer_follows_it()
    {
        var window = Window();
        window.Show(Items(100));

        for (var step = 0; step < 6; step++)
        {
            window.ShowEarlier();
        }

        window.Shown.Should().Equal(Items(100).Skip(60).Take(30));
        (window.Earlier, window.Later).Should().Be((60, 10));

        window.Show(Items(105));
        window.Shown.Should().Equal(Items(100).Skip(60).Take(30), "what arrives at the end is not on a page that left it");
        window.Later.Should().Be(15);
    }

    [Fact]
    public void Coming_forward_shows_a_step_more_and_follows_the_end_once_it_is_reached()
    {
        var window = Window();
        window.Show(Items(100));
        window.Reach("item-20");

        while (window.ShowLater())
        {
        }

        window.Shown.Should().Equal(Items(100).Skip(70));
        window.Show(Items(101));
        window.Shown[^1].Should().Be("item-100");
    }

    [Fact]
    public void An_item_near_the_page_is_reached_by_growing_to_it()
    {
        var window = Window();
        window.Show(Items(100));

        window.Reach("item-85").Should().BeTrue();

        window.Shown.Should().Equal(Items(100).Skip(80));
    }

    [Fact]
    public void An_item_far_from_the_page_is_reached_by_standing_around_it()
    {
        var window = Window();
        window.Show(Items(1000));

        window.Reach("item-400").Should().BeTrue();

        window.Shown.Should().Equal(Items(1000).Skip(395).Take(11));
        window.Shown.Count.Should().BeLessThanOrEqualTo(window.Most);
    }

    [Fact]
    public void An_item_on_the_page_or_not_listed_changes_nothing()
    {
        var window = Window();
        window.Show(Items(100));

        window.Reach("item-95").Should().BeFalse();
        window.Reach("item-none").Should().BeFalse();

        window.Shown.Should().Equal(Items(100).Skip(90));
    }

    [Fact]
    public void The_newest_joins_a_page_near_it_and_replaces_one_far_from_it()
    {
        // Five steps back let go of the last five items: the newest is near, and the page grows back to it.
        var near = Window();
        near.Show(Items(100));
        for (var step = 0; step < 5; step++)
        {
            near.ShowEarlier();
        }

        near.Later.Should().Be(5);
        near.ShowNewest().Should().BeTrue();
        near.Shown.Should().Equal(Items(100).Skip(70));

        var far = Window();
        far.Show(Items(1000));
        far.Reach("item-400");
        far.ShowNewest().Should().BeTrue();
        far.Shown.Should().Equal(Items(1000).Skip(990));
        far.ShowNewest().Should().BeFalse("the page ends with the newest");
    }

    [Fact]
    public void A_group_is_never_cut_at_either_end()
    {
        // Groups of four: an item opens one when its number divides by four.
        var window = new ListWindow<string>(item => item, item => Number(item) % 4 == 0) { Page = 10, Step = 3, Most = 30 };
        window.Show(Items(100));

        window.Shown[0].Should().Be("item-88", "the page begins where a group does");

        window.Reach("item-41");

        window.Shown[0].Should().Be("item-36");
        window.Shown[^1].Should().Be("item-47", "the page ends where a group does");
    }

    [Fact]
    public void A_list_that_no_longer_holds_the_page_is_shown_from_its_newest()
    {
        var window = Window();
        window.Show(Items(100));
        window.Reach("item-20");

        window.Show([.. Enumerable.Range(0, 50).Select(index => $"other-{index}")]);

        window.Shown[^1].Should().Be("other-49");
        (window.Earlier, window.Later).Should().Be((40, 0));
    }

    [Fact]
    public void A_page_keeps_its_items_when_others_leave_or_arrive_before_it()
    {
        var window = Window();
        window.Show(Items(100));
        window.Reach("item-50");
        string[] held = [.. window.Shown];

        window.Show([.. Items(100).Skip(10)]);

        window.Shown.Should().Equal(held);
        window.Earlier.Should().Be(35);
    }

    #endregion

    #region Private Methods

    private static ListWindow<string> Window() => new(item => item) { Page = 10, Step = 5, Most = 30 };

    private static List<string> Items(int count) => [.. Enumerable.Range(0, count).Select(index => $"item-{index}")];

    private static int Number(string item) => int.Parse(item.AsSpan(5), CultureInfo.InvariantCulture);

    #endregion
}
