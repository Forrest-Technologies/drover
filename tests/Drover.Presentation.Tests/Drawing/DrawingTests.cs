namespace Drover.Presentation.Tests.Drawing;

/// <summary>The readers that turn the drawings kept as data into shapes, and the color mixing the bot characters use.</summary>
public sealed class DrawingTests
{
    #region Public Methods

    [Fact]
    public void Every_icon_reads_as_paths_and_nothing_else()
    {
        foreach (var name in Enum.GetValues<IconName>())
        {
            var shapes = SvgParts.Read(IconShapes.For(name));

            shapes.Should().NotBeEmpty($"{name} has a shape");
            shapes.Should().AllBeOfType<SvgPath>();
        }
    }

    [Fact]
    public void Every_part_of_every_bot_character_can_be_read()
    {
        for (uint seed = 0; seed < 6 * 6 * 6 * 6 * 4; seed += 7)
        {
            var look = BotLook.From(seed);

            SvgParts.Read(look.Visor).Should().NotBeEmpty();
            SvgParts.Read(look.Top).Should().NotBeNull();
            SvgParts.Read(look.Mark).Should().NotBeNull();
            look.Head.Should().StartWith("M");
        }
    }

    [Fact]
    public void A_shape_keeps_the_class_that_says_what_role_it_plays()
    {
        var shapes = SvgParts.Read("""<path class="dv-character-line" d="M41 14L46 3"/><circle class="dv-character-dot" cx="46.5" cy="3.5" r="3"/>""");

        shapes.Select(shape => shape.Class).Should().Equal("dv-character-line", "dv-character-dot");
        shapes[1].Should().Be(new SvgCircle(46.5, 3.5, 3, "dv-character-dot"));
    }

    [Fact]
    public void A_rectangle_and_a_circle_are_restated_as_paths_so_one_kind_of_code_draws_everything()
    {
        new SvgRect(4, 27, 56, 6, 0, null).PathData.Should().Be("M4 27H60V33H4Z");
        new SvgRect(4, 27, 56, 6, 3, null).PathData.Should().Be("M7 27H57A3 3 0 0 1 60 30V30A3 3 0 0 1 57 33H7A3 3 0 0 1 4 30V30A3 3 0 0 1 7 27Z");
        new SvgCircle(32, 29, 9.5, null).PathData.Should().Be("M22.5 29A9.5 9.5 0 1 0 41.5 29A9.5 9.5 0 1 0 22.5 29Z");
    }

    [Fact]
    public void Nothing_reads_as_no_shapes_and_an_unknown_element_is_refused()
    {
        SvgParts.Read(string.Empty).Should().BeEmpty();

        var act = () => SvgParts.Read("""<text>sample</text>""");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void A_color_reads_from_and_writes_to_the_form_the_tokens_use()
    {
        Rgb.Parse("#05070A").Should().Be(new Rgb(5, 7, 10));
        new Rgb(255, 180, 84).ToString().Should().Be("#FFB454");
    }

    [Fact]
    public void Mixing_keeps_a_color_whole_at_a_full_share_and_gives_the_other_at_none()
    {
        var head = Rgb.Parse("#904961");
        var visor = Rgb.Parse("#05070A");

        head.MixWith(visor, 1).Should().Be(head);
        head.MixWith(visor, 0).Should().Be(visor);
    }

    [Fact]
    public void Mixing_happens_in_oklab_as_the_web_clients_style_sheets_mix()
    {
        // Half black and half white in Oklab is the gray a browser gives for color-mix(in oklab, black, white).
        Rgb.Parse("#000000").MixWith(Rgb.Parse("#FFFFFF"), 0.5).Should().Be(Rgb.Parse("#636363"));
    }

    #endregion
}
