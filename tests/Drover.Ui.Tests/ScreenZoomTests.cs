namespace Drover.Ui.Tests;

/// <summary>
/// The sandbox picture enlarged and moved: where a tap lands at any size, zoom, and offset, up to the edges, and how
/// the zoom buttons, the arrow keys, a pinch's result, and the keyboard pointer change what shows.
/// </summary>
public sealed class ScreenZoomTests
{
    #region Where A Tap Lands

    [Theory]
    [InlineData(328, 246, 1, 0, 0, 164, 123, 512, 384)]
    [InlineData(400, 300, 2, -0.5, -0.5, 0, 0, 256, 192)]
    [InlineData(400, 300, 2, -0.5, -0.5, 200, 150, 512, 384)]
    [InlineData(400, 300, 2, -0.5, -0.5, 400, 300, 768, 576)]
    [InlineData(380, 285, 1.5, -0.25, 0, 190, 142.5, 512, 256)]
    [InlineData(1024, 768, 3, -1, -2, 512, 384, 512, 640)]
    [InlineData(328, 246, 4, -3, -3, 0, 0, 768, 576)]
    [InlineData(328, 246, 4, -3, -3, 327.9, 245.9, 1023, 767)]
    [InlineData(328, 246, 4, 0, 0, 0, 0, 0, 0)]
    [InlineData(328, 246, 4, 0, 0, 82, 61.5, 64, 48)]
    public void A_tap_on_an_enlarged_and_moved_picture_lands_on_the_sandbox_pixel_under_it(
        double shownWidth,
        double shownHeight,
        double scale,
        double x,
        double y,
        double offsetX,
        double offsetY,
        int screenX,
        int screenY)
    {
        ScreenPoint.FromShown(offsetX, offsetY, shownWidth, shownHeight, 1024, 768, new ScreenZoom(scale, x, y))
            .Should().Be((screenX, screenY));
    }

    [Theory]
    [InlineData(2, 0, 0, -10, -10, 0, 0)]
    [InlineData(2, -1, -1, 410, 310, 1023, 767)]
    [InlineData(4, -3, 0, 401, -1, 1023, 0)]
    public void A_tap_past_the_frame_edge_at_any_zoom_stays_on_the_screen(
        double scale,
        double x,
        double y,
        double offsetX,
        double offsetY,
        int screenX,
        int screenY)
    {
        ScreenPoint.FromShown(offsetX, offsetY, 400, 300, 1024, 768, new ScreenZoom(scale, x, y)).Should().Be((screenX, screenY));
    }

    [Fact]
    public void At_fit_a_tap_lands_where_it_did_before_there_was_a_zoom()
    {
        ScreenPoint.FromShown(95, 71.25, 380, 285, 1024, 768, ScreenZoom.Fit)
            .Should().Be(ScreenPoint.FromShown(95, 71.25, 380, 285, 1024, 768));
    }

    #endregion

    #region Zoom And Pan

    [Fact]
    public void The_zoom_buttons_step_in_and_out_around_the_middle_of_what_shows()
    {
        List<ScreenZoom> steps = [];
        var zoom = ScreenZoom.Fit;
        for (var press = 0; press < 5; press++)
        {
            zoom = zoom.ZoomedIn();
            steps.Add(zoom);
        }

        steps.Select(step => step.Scale).Should().Equal(1.5, 2, 3, 4, 4);
        steps[1].Should().Be(new ScreenZoom(2, -0.5, -0.5));
        steps[3].Percent.Should().Be("400%");
        steps[3].IsMax.Should().BeTrue();
        zoom.ZoomedOut().Scale.Should().Be(3);
        new ScreenZoom(2, -0.5, -0.5).ZoomedOut().ZoomedOut().Should().Be(ScreenZoom.Fit);
    }

    [Fact]
    public void A_pinch_scale_between_steps_zooms_to_the_next_step_either_way()
    {
        new ScreenZoom(1.7, 0, 0).ZoomedIn().Scale.Should().Be(2);
        new ScreenZoom(1.7, 0, 0).ZoomedOut().Scale.Should().Be(1.5);
        new ScreenZoom(1.7, 0, 0).Percent.Should().Be("170%");
    }

    [Fact]
    public void Zooming_around_a_corner_keeps_that_corner_in_place()
    {
        ScreenZoom.Fit.ZoomedTo(3, 0, 0).Should().Be(new ScreenZoom(3, 0, 0));
        ScreenZoom.Fit.ZoomedTo(3, 1, 1).Should().Be(new ScreenZoom(3, -2, -2));
    }

    [Theory]
    [InlineData(10, 0, 0, 4, 0, 0)]
    [InlineData(0.5, -0.3, -0.3, 1, 0, 0)]
    [InlineData(2, 0.4, -1.6, 2, 0, -1)]
    [InlineData(2, -0.25, -0.75, 2, -0.25, -0.75)]
    [InlineData(double.NaN, double.NaN, double.PositiveInfinity, 1, 0, 0)]
    public void A_view_is_kept_inside_its_bounds(double scale, double x, double y, double keptScale, double keptX, double keptY)
    {
        new ScreenZoom(scale, x, y).Clamped().Should().Be(new ScreenZoom(keptScale, keptX, keptY));
    }

    [Theory]
    [InlineData("ArrowRight", false, -0.6, -0.5)]
    [InlineData("ArrowRight", true, -0.9, -0.5)]
    [InlineData("ArrowLeft", false, -0.4, -0.5)]
    [InlineData("ArrowUp", true, -0.5, -0.1)]
    [InlineData("ArrowDown", true, -0.5, -0.9)]
    public void An_arrow_key_moves_the_view_that_way(string key, bool far, double x, double y)
    {
        var moved = new ScreenZoom(2, -0.5, -0.5).Moved(key, far);

        moved!.Value.X.Should().BeApproximately(x, 1e-9);
        moved.Value.Y.Should().BeApproximately(y, 1e-9);
    }

    [Fact]
    public void The_view_stops_at_the_edge_and_other_keys_do_not_move_it()
    {
        new ScreenZoom(2, 0, -1).Moved("ArrowLeft", true).Should().Be(new ScreenZoom(2, 0, -1));
        new ScreenZoom(2, 0, -1).Moved("ArrowDown", true).Should().Be(new ScreenZoom(2, 0, -1));
        new ScreenZoom(2, 0, -1).Moved("Enter", false).Should().BeNull();
        ScreenZoom.Fit.Moved("ArrowRight", true).Should().Be(ScreenZoom.Fit);
    }

    [Fact]
    public void The_view_follows_a_pointer_that_leaves_it_and_stays_put_while_it_shows()
    {
        var zoom = new ScreenZoom(2, 0, 0);

        zoom.Showing(0.25, 0.25).Should().Be(zoom);
        var followed = zoom.Showing(0.9, 0.1);
        followed.X.Should().BeApproximately(-0.9, 1e-9);
        followed.Y.Should().Be(0);
        zoom.Showing(1, 1).Should().Be(new ScreenZoom(2, -1, -1));
        ScreenZoom.Fit.Showing(0.99, 0.01).Should().Be(ScreenZoom.Fit);
    }

    #endregion
}
