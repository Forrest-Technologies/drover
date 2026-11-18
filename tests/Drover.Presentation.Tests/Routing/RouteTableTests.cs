namespace Drover.Presentation.Tests.Routing;

/// <summary>Which screen an address opens, and what the address carries.</summary>
public sealed class RouteTableTests
{
    #region Private Fields

    private static readonly Guid bot = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly Guid thread = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static readonly RouteTable<string> table = new RouteTable<string>()
        .Map("talk", "/", "/talk/{BotId:guid}", "/talk/{BotId:guid}/{Rail}", "/talk/{BotId:guid}/side/{SideChat}",
            "/talk/{BotId:guid}/activity/{ActivityRunId:guid}", "/talk/{BotId:guid}/work/{WorkBotId:guid}/{WorkThreadId:guid}")
        .Map("bots", "/bots")
        .Map("bot", "/bots/{BotId:guid}")
        .Map("sandbox", "/bots/{BotId:guid}/sandbox")
        .Map("tools", "/tools")
        .Map("usage", "/usage");

    #endregion

    #region Public Methods

    [Fact]
    public void The_start_address_opens_its_screen_with_nothing_in_it()
    {
        var match = table.Match(Addresses.Home);

        match!.Screen.Should().Be("talk");
        match.Parameters.Should().BeEmpty();
        match.Query.Should().BeEmpty();
        match.Fragment.Should().BeNull();
    }

    [Fact]
    public void An_id_in_the_path_is_read_as_a_parameter()
    {
        var match = table.Match(Addresses.Bot(bot));

        match!.Screen.Should().Be("bot");
        match.GuidParameter("BotId").Should().Be(bot);
    }

    [Fact]
    public void A_literal_segment_wins_over_a_parameter_at_the_same_place()
    {
        table.Match(Addresses.SideChats(bot))!.Parameter("Rail").Should().Be("side");
        table.Match(Addresses.NewSideChat(bot))!.Parameter("SideChat").Should().Be("new");
        table.Match(Addresses.SideChat(bot, thread))!.GuidParameter("SideChat").Should().Be(thread);
        table.Match(Addresses.Activity(bot, thread))!.GuidParameter("ActivityRunId").Should().Be(thread);
        table.Match(Addresses.BotSandbox(bot))!.Screen.Should().Be("sandbox");
    }

    [Fact]
    public void A_segment_that_is_not_an_id_does_not_fit_a_place_that_takes_one()
    {
        table.Match("/bots/not-an-id").Should().BeNull();
        table.Match("/talk/not-an-id/summary").Should().BeNull();
    }

    [Fact]
    public void The_query_is_read_by_name_and_unescaped()
    {
        var usage = table.Match(Addresses.UsageFor("30d", bot));
        usage!.QueryValue("period").Should().Be("30d");
        usage.QueryValue("bot").Should().Be(bot.ToString());

        var ticket = table.Match(Addresses.ToolSignInReturn("a ticket/with+marks&more"));
        ticket!.Screen.Should().Be("tools");
        ticket.QueryValue("ticket").Should().Be("a ticket/with+marks&more");

        table.Match(Addresses.BotMemory(bot))!.QueryValue("section").Should().Be("memory");
        table.Match(Addresses.BotTerminal(bot))!.QueryValue("tab").Should().Be("terminal");
    }

    [Fact]
    public void The_fragment_is_kept_apart_from_the_path_and_the_query()
    {
        var message = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var moment = table.Match(Addresses.Moment(bot, thread, message, inMainConversation: true));

        moment!.Screen.Should().Be("talk");
        moment.GuidParameter("BotId").Should().Be(bot);
        moment.Fragment.Should().Be($"message-{message}");
    }

    [Fact]
    public void An_address_no_screen_is_at_matches_nothing()
    {
        table.Match("/nowhere").Should().BeNull();
        table.Match("/bots/extra/segments/here").Should().BeNull();
    }

    [Fact]
    public void A_trailing_slash_and_letter_case_do_not_change_the_screen()
    {
        table.Match("/bots/")!.Screen.Should().Be("bots");
        table.Match("/Bots")!.Screen.Should().Be("bots");
    }

    [Fact]
    public void One_pattern_cannot_be_given_to_two_screens()
    {
        var act = () => new RouteTable<string>().Map("one", "/same").Map("two", "/same");

        act.Should().Throw<ArgumentException>();
    }

    #endregion
}
