namespace Drover.Presentation.Tests.ViewModels.Bots;

/// <summary>
/// The touch model of the sandbox screen in control: what each gesture sends to the sandbox and what it does to the
/// view. The frame is 400 by 300; times are from the moment the first finger went down.
/// </summary>
public sealed class ScreenGestureTests
{
    #region Private Fields

    private const double Width = 400;

    private const double Height = 300;

    private static readonly ScreenZoom enlarged = new(2, -0.5, -0.5);

    private readonly ScreenGesture gesture = new();

    #endregion

    #region Tap and long press

    [Fact]
    public void A_tap_is_a_click_where_the_finger_went_down_and_changes_no_view()
    {
        Press(1, 100, 100);
        gesture.Move(1, 104, 103, Width, Height, Ms(80)).Should().BeNull("it has not left the place it went down");

        gesture.Up(1, at: Ms(120)).Should().Be(default(ScreenGestureEnd), "in control a tap is sent, not reported");

        Sent().Should().Equal(new ScreenTouch(ScreenTouchKind.Tap, 100, 100));
        gesture.IsActive.Should().BeFalse();
    }

    [Fact]
    public void A_press_held_still_for_the_long_press_time_is_a_right_click_when_it_lifts()
    {
        Press(1, 100, 100);

        gesture.IsLongPress(Ms(599)).Should().BeFalse();
        gesture.IsLongPress(Ms(600)).Should().BeTrue("the screen can say a lift now right-clicks");
        Sent().Should().BeEmpty("nothing is sent until the finger lifts");

        gesture.Up(1, at: Ms(700));

        Sent().Should().Equal(new ScreenTouch(ScreenTouchKind.LongPress, 100, 100));
    }

    [Fact]
    public void A_press_lifted_between_the_hold_and_the_long_press_is_still_a_click()
    {
        Press(1, 100, 100);

        gesture.Up(1, at: Ms(450));

        Sent().Should().Equal(new ScreenTouch(ScreenTouchKind.Tap, 100, 100));
    }

    [Fact]
    public void A_press_the_system_takes_away_sends_nothing()
    {
        Press(1, 100, 100);

        gesture.Up(1, cancelled: true, at: Ms(900)).Should().Be(default(ScreenGestureEnd));

        Sent().Should().BeEmpty("a cancelled press is neither a click nor a right click");
        gesture.Up(7).Should().Be(default(ScreenGestureEnd), "a pointer that never went down here ends nothing");
    }

    #endregion

    #region Drag

    [Fact]
    public void A_move_past_the_slop_on_a_picture_at_fit_drags_with_the_left_button_from_where_it_went_down()
    {
        Press(1, 100, 100);

        gesture.Move(1, 100 + ScreenGesture.TapSlop - 1, 100, Width, Height, Ms(40)).Should().BeNull();
        Sent().Should().BeEmpty("still inside the slop");

        gesture.Move(1, 120, 100, Width, Height, Ms(60)).Should().BeNull("a drag in control never moves the view");
        gesture.IsDragging.Should().BeTrue();
        gesture.Move(1, 180, 140, Width, Height, Ms(90));
        gesture.Up(1, at: Ms(120)).Should().Be(default(ScreenGestureEnd));

        Sent().Should().Equal(
            new ScreenTouch(ScreenTouchKind.Press, 100, 100),
            new ScreenTouch(ScreenTouchKind.Drag, 120, 100),
            new ScreenTouch(ScreenTouchKind.Drag, 180, 140),
            new ScreenTouch(ScreenTouchKind.Release, 180, 140));
    }

    [Fact]
    public void A_drag_does_not_send_a_move_that_goes_nowhere_and_never_clicks()
    {
        Press(1, 100, 100);
        gesture.Move(1, 150, 100, Width, Height, Ms(50));
        gesture.Move(1, 150, 100, Width, Height, Ms(60));
        gesture.Up(1, at: Ms(900));

        Sent().Select(touch => touch.Kind).Should().Equal(
            ScreenTouchKind.Press, ScreenTouchKind.Drag, ScreenTouchKind.Release);
    }

    [Fact]
    public void On_an_enlarged_picture_a_quick_move_pans_the_view_and_sends_nothing()
    {
        Press(1, 200, 150, enlarged);

        gesture.Move(1, 160, 150, Width, Height, Ms(100)).Should().Be(new ScreenZoom(2, -0.6, -0.5));
        gesture.Up(1, at: Ms(200)).Should().Be(new ScreenGestureEnd(false, new ScreenZoom(2, -0.6, -0.5)));

        Sent().Should().BeEmpty("a pan is the person's own view and never clicks");
    }

    [Fact]
    public void On_an_enlarged_picture_a_move_after_the_hold_drags_instead_of_panning()
    {
        Press(1, 200, 150, enlarged);

        gesture.Move(1, 200, 150 + ScreenGesture.TapSlop, Width, Height, ScreenGesture.HoldTime).Should().BeNull();
        gesture.Up(1, at: Ms(500)).Should().Be(default(ScreenGestureEnd), "a drag keeps the view as it was");

        Sent().Select(touch => touch.Kind).Should().Equal(
            ScreenTouchKind.Press, ScreenTouchKind.Drag, ScreenTouchKind.Release);
    }

    [Fact]
    public void A_drag_the_system_takes_away_still_releases_the_button_where_the_finger_was()
    {
        Press(1, 100, 100);
        gesture.Move(1, 160, 100, Width, Height, Ms(50));
        Sent().Should().Equal(
            new ScreenTouch(ScreenTouchKind.Press, 100, 100),
            new ScreenTouch(ScreenTouchKind.Drag, 160, 100));

        gesture.Up(1, cancelled: true, at: Ms(80));

        Sent().Should().Equal(new ScreenTouch(ScreenTouchKind.Release, 160, 100));
        gesture.IsDragging.Should().BeFalse();
    }

    [Fact]
    public void Losing_the_focus_or_control_during_a_drag_releases_the_button_once_and_ends_the_gesture()
    {
        Press(1, 100, 100);
        gesture.Move(1, 160, 120, Width, Height, Ms(50));
        Sent();

        gesture.Release().Should().BeTrue();

        Sent().Should().Equal(new ScreenTouch(ScreenTouchKind.Release, 160, 120));
        gesture.IsActive.Should().BeFalse();
        gesture.Release().Should().BeFalse("nothing is held any more");
        gesture.Up(1, at: Ms(90)).Should().Be(default(ScreenGestureEnd));
        Sent().Should().BeEmpty("the finger lifting afterwards sends nothing");
    }

    [Fact]
    public void Ending_a_gesture_that_holds_no_button_sends_nothing()
    {
        Press(1, 100, 100);

        gesture.Release().Should().BeFalse();

        Sent().Should().BeEmpty("a press without a drag is not a click when the gesture is ended for it");
    }

    [Fact]
    public void A_second_finger_during_a_drag_releases_the_button_and_the_rest_of_the_gesture_does_nothing()
    {
        Press(1, 100, 100);
        gesture.Move(1, 160, 100, Width, Height, Ms(50));
        Sent();

        gesture.Down(2, 300, 200, ScreenZoom.Fit, at: Ms(60));
        Sent().Should().Equal(new ScreenTouch(ScreenTouchKind.Release, 160, 100));

        gesture.Move(2, 340, 120, Width, Height, Ms(80)).Should().BeNull("no pinch or scroll after a drag");
        gesture.Move(1, 100, 40, Width, Height, Ms(90)).Should().BeNull();
        gesture.Up(2, at: Ms(100));
        gesture.Up(1, at: Ms(110)).Should().Be(default(ScreenGestureEnd));

        Sent().Should().BeEmpty("no click, no second drag, no wheel");
    }

    #endregion

    #region Two fingers

    [Fact]
    public void Two_fingers_moving_up_together_turn_the_wheel_down_one_step_for_each_scroll_step()
    {
        Press(1, 150, 200);
        gesture.Down(2, 250, 200, ScreenZoom.Fit, at: Ms(10));

        gesture.Move(1, 150, 200 - ScreenGesture.TapSlop + 1, Width, Height, Ms(20)).Should().BeNull();
        Sent().Should().BeEmpty("inside the slop it is not a scroll yet");

        MoveBoth((150, 191), (250, 200), (150, 140), (250, 140), Ms(30));
        gesture.IsScrolling.Should().BeTrue();

        Sent().Should().OnlyContain(touch => touch.Kind == ScreenTouchKind.ScrollDown)
            .And.HaveCount((int)(60 / ScreenGesture.ScrollStep));
    }

    [Fact]
    public void Two_fingers_moving_down_turn_the_wheel_up_and_a_scroll_never_clicks_or_moves_the_view()
    {
        Press(1, 150, 100);
        gesture.Down(2, 250, 100, ScreenZoom.Fit, at: Ms(10));

        MoveBoth((150, 100), (250, 100), (150, 160), (250, 160), Ms(30)).Should().BeTrue("no step changes the view");
        gesture.Up(2, at: Ms(60)).Should().Be(default(ScreenGestureEnd));
        gesture.Move(1, 150, 220, Width, Height, Ms(70)).Should().BeNull("a finger left after a scroll does nothing");
        gesture.Up(1, at: Ms(80)).Should().Be(default(ScreenGestureEnd));

        Sent().Should().NotBeEmpty().And.OnlyContain(touch => touch.Kind == ScreenTouchKind.ScrollUp);
    }

    [Fact]
    public void Two_fingers_spreading_apart_pinch_the_view_and_send_nothing()
    {
        Press(1, 150, 150);
        gesture.Down(2, 250, 150, ScreenZoom.Fit, at: Ms(10));

        var shown = gesture.Move(2, 350, 150, Width, Height, Ms(40));

        shown.Should().NotBeNull();
        shown!.Value.Scale.Should().Be(2);
        ScreenPoint.FromShown(250, 150, Width, Height, 1024, 768, shown.Value)
            .Should().Be((512, 384), "what was between the fingers still is");
        gesture.Up(2, at: Ms(60)).Should().Be(default(ScreenGestureEnd), "one finger is still down");
        var end = gesture.Up(1, at: Ms(900));

        end.View!.Value.Scale.Should().Be(2);
        Sent().Should().BeEmpty("a pinch never clicks, even after the long press time");
    }

    [Fact]
    public void A_pinch_keeps_panning_with_the_finger_left_as_when_watching()
    {
        Press(1, 150, 150);
        gesture.Down(2, 250, 150, ScreenZoom.Fit, at: Ms(10));
        gesture.Move(2, 350, 150, Width, Height, Ms(40));
        gesture.Up(2, at: Ms(50));

        gesture.Move(1, 100, 150, Width, Height, Ms(70)).Should().NotBeNull("the view follows the finger left");

        gesture.Up(1, at: Ms(80)).View.Should().NotBeNull();
        Sent().Should().BeEmpty();
    }

    [Fact]
    public void A_scroll_stays_a_scroll_when_the_fingers_spread_later()
    {
        Press(1, 150, 200);
        gesture.Down(2, 250, 200, ScreenZoom.Fit, at: Ms(10));
        MoveBoth((150, 200), (250, 200), (150, 150), (250, 150), Ms(20));

        gesture.Move(2, 390, 150, Width, Height, Ms(40)).Should().BeNull("a scroll does not turn into a zoom");

        gesture.IsScrolling.Should().BeTrue();
    }

    [Fact]
    public void Two_fingers_put_down_and_lifted_without_moving_send_nothing()
    {
        Press(1, 150, 200);
        gesture.Down(2, 250, 200, ScreenZoom.Fit, at: Ms(10));

        gesture.Up(1, at: Ms(50));
        gesture.Up(2, at: Ms(900)).Should().Be(default(ScreenGestureEnd));

        Sent().Should().BeEmpty("neither a click nor a right click");
    }

    [Fact]
    public void A_third_finger_during_a_scroll_turns_the_wheel_no_further_whatever_the_fingers_do_next()
    {
        Press(1, 150, 200);
        gesture.Down(2, 250, 200, ScreenZoom.Fit, at: Ms(10));
        MoveBoth((150, 200), (250, 200), (150, 150), (250, 150), Ms(20));
        Sent();

        gesture.Down(3, 200, 20, ScreenZoom.Fit, at: Ms(30));
        gesture.Move(3, 200, 290, Width, Height, Ms(40)).Should().BeNull();
        gesture.Move(1, 150, 40, Width, Height, Ms(50)).Should().BeNull();
        gesture.Up(3, at: Ms(60));
        gesture.Move(2, 250, 20, Width, Height, Ms(70)).Should().BeNull();
        gesture.Up(1, at: Ms(80));
        gesture.Up(2, at: Ms(90)).Should().Be(default(ScreenGestureEnd));

        Sent().Should().BeEmpty("the jump of the place between the fingers is not a scroll");
        gesture.IsScrolling.Should().BeFalse();
    }

    [Fact]
    public void A_third_finger_landing_before_two_fingers_moved_sends_nothing_at_all()
    {
        Press(1, 150, 200);
        gesture.Down(2, 250, 200, ScreenZoom.Fit, at: Ms(10));
        gesture.Down(3, 200, 20, ScreenZoom.Fit, at: Ms(20));

        MoveBoth((150, 200), (250, 200), (150, 60), (250, 60), Ms(30)).Should().BeTrue();
        gesture.Up(3, at: Ms(40));
        gesture.Up(2, at: Ms(50));
        gesture.Up(1, at: Ms(900));

        Sent().Should().BeEmpty("neither a scroll nor a click nor a right click");
    }

    [Fact]
    public void A_third_finger_during_a_pinch_keeps_the_view_the_pinch_reached_and_moves_it_no_further()
    {
        Press(1, 150, 150);
        gesture.Down(2, 250, 150, ScreenZoom.Fit, at: Ms(10));
        var pinched = gesture.Move(2, 350, 150, Width, Height, Ms(40));

        gesture.Down(3, 50, 50, ScreenZoom.Fit, at: Ms(50));
        gesture.Move(3, 390, 290, Width, Height, Ms(60)).Should().BeNull();
        gesture.Up(3, at: Ms(70));
        gesture.Up(2, at: Ms(80));

        gesture.Up(1, at: Ms(90)).Should().Be(new ScreenGestureEnd(false, pinched));
        Sent().Should().BeEmpty();
    }

    [Fact]
    public void A_drag_ended_by_a_second_finger_is_released_once_however_many_fingers_follow()
    {
        Press(1, 100, 100);
        gesture.Move(1, 160, 100, Width, Height, Ms(50));
        gesture.Down(2, 300, 200, ScreenZoom.Fit, at: Ms(60));
        gesture.Down(3, 200, 20, ScreenZoom.Fit, at: Ms(70));
        gesture.Release();
        gesture.Up(3, at: Ms(80));
        gesture.Up(2, at: Ms(90));
        gesture.Up(1, at: Ms(100));

        Sent().Select(touch => touch.Kind).Should().Equal(
            ScreenTouchKind.Press, ScreenTouchKind.Drag, ScreenTouchKind.Release);
    }

    #endregion

    #region Robustness

    public static TheoryData<string> DragExits() =>
        ["lift", "cancel", "second finger", "third finger", "release", "second finger cancelled", "release twice"];

    [Theory]
    [MemberData(nameof(DragExits))]
    public void Every_press_handed_over_is_followed_by_exactly_one_release_whichever_way_the_drag_ends(string exit)
    {
        Press(1, 100, 100);
        gesture.Move(1, 160, 100, Width, Height, Ms(50));
        List<ScreenTouch> handed = [.. Sent()];
        handed[0].Kind.Should().Be(ScreenTouchKind.Press, "the press is handed over before the drag ends");

        switch (exit)
        {
            case "lift":
                gesture.Up(1, at: Ms(80));
                break;
            case "cancel":
                gesture.Up(1, cancelled: true, at: Ms(80));
                break;
            case "second finger":
                gesture.Down(2, 300, 200, ScreenZoom.Fit, at: Ms(60));
                break;
            case "third finger":
                gesture.Down(2, 300, 200, ScreenZoom.Fit, at: Ms(60));
                gesture.Down(3, 200, 20, ScreenZoom.Fit, at: Ms(70));
                break;
            case "release":
                gesture.Release();
                break;
            case "second finger cancelled":
                gesture.Down(2, 300, 200, ScreenZoom.Fit, at: Ms(60));
                gesture.Up(2, cancelled: true, at: Ms(70));
                break;
            case "release twice":
                gesture.Release();
                gesture.Release();
                break;
        }

        gesture.Release();
        gesture.Up(1, at: Ms(200));
        gesture.Up(2, at: Ms(210));
        gesture.Up(3, at: Ms(220));
        handed.AddRange(Sent());

        handed.Count(touch => touch.Kind == ScreenTouchKind.Press).Should().Be(1);
        handed.Count(touch => touch.Kind == ScreenTouchKind.Release).Should().Be(1);
        handed[^1].Kind.Should().Be(ScreenTouchKind.Release, "nothing follows the release");
        handed.Select(touch => touch.Kind).Should().NotContain([ScreenTouchKind.Tap, ScreenTouchKind.LongPress]);
    }

    [Fact]
    public void A_second_down_for_a_finger_already_down_changes_nothing()
    {
        Press(1, 100, 100);
        gesture.Move(1, 160, 100, Width, Height, Ms(50));

        gesture.Down(1, 300, 280, ScreenZoom.Fit, inControl: true, at: Ms(60));
        gesture.Up(1, at: Ms(80));

        Sent().Should().Equal(
            new ScreenTouch(ScreenTouchKind.Press, 100, 100),
            new ScreenTouch(ScreenTouchKind.Drag, 160, 100),
            new ScreenTouch(ScreenTouchKind.Release, 160, 100));
    }

    [Theory]
    [InlineData(double.NaN, 300)]
    [InlineData(400, double.NaN)]
    [InlineData(double.PositiveInfinity, 300)]
    [InlineData(400, double.PositiveInfinity)]
    public void A_frame_whose_size_is_not_a_number_moves_no_view_and_sends_nothing(double width, double height)
    {
        gesture.Down(1, 200, 150, enlarged, at: Ms(0));
        gesture.Move(1, 160, 150, width, height, Ms(50)).Should().BeNull();
        gesture.Up(1, at: Ms(60)).View.Should().Match<ScreenZoom?>(
            view => view == null || view == enlarged,
            "the view is kept as it was, never made invalid");

        Press(1, 100, 100, enlarged);
        gesture.Move(1, 60, 100, width, height, Ms(50)).Should().BeNull();
        Sent().Should().BeEmpty("a move over a frame of no valid size is not a drag or a pan");
        gesture.IsDragging.Should().BeFalse();

        gesture.Up(1, at: Ms(60));
        Sent().Should().Equal(
            [new ScreenTouch(ScreenTouchKind.Tap, 100, 100)],
            "the finger never moved as far as the gesture knows, so it lifts as a tap where it went down");
    }

    // Two fingers that move together, at places so far out that adding them overflows a number.
    [Theory]
    [InlineData(1.7e308, 1.6e308, 200, 100)]
    [InlineData(-1.7e308, -1.6e308, 200, 260)]
    public void Fingers_at_the_edge_of_what_a_number_holds_send_only_finite_places_and_at_most_one_scroll_a_move(
        double firstX,
        double secondX,
        double y,
        double toY)
    {
        Press(1, firstX, y);
        gesture.Down(2, secondX, y, ScreenZoom.Fit, at: Ms(10));
        List<ScreenTouch> handed = [];

        for (var step = 1; step <= 10; step++)
        {
            var at = y + ((toY - y) * step / 10);
            gesture.Move(1, firstX, at, Width, Height, Ms(20 + step));
            handed.AddRange(Sent().Should().HaveCountLessThanOrEqualTo(ScreenGesture.MaxStepsPerMove).And.Subject);
            gesture.Move(2, secondX, at, Width, Height, Ms(20 + step));
            handed.AddRange(Sent().Should().HaveCountLessThanOrEqualTo(ScreenGesture.MaxStepsPerMove).And.Subject);
        }

        gesture.Up(2, at: Ms(40));
        gesture.Up(1, at: Ms(50));
        handed.AddRange(Sent());

        handed.Should().NotBeEmpty("the fingers moved together far enough to scroll")
            .And.OnlyContain(touch => double.IsFinite(touch.X) && double.IsFinite(touch.Y));
    }

    [Fact]
    public void Fingers_whose_centre_moves_by_more_than_a_number_holds_turn_the_wheel_at_most_one_scroll_a_move()
    {
        Press(1, 150, 1.7e308);
        gesture.Down(2, 250, 1.7e308, ScreenZoom.Fit, at: Ms(10));

        gesture.Move(1, 150, 1.6e308, Width, Height, Ms(20));
        Sent().Should().HaveCountLessThanOrEqualTo(ScreenGesture.MaxStepsPerMove);
        gesture.Move(2, 250, 1.6e308, Width, Height, Ms(30));
        Sent().Should().HaveCountLessThanOrEqualTo(ScreenGesture.MaxStepsPerMove)
            .And.OnlyContain(touch => double.IsFinite(touch.X) && double.IsFinite(touch.Y));
    }

    // The fingers are so far apart that their spread overflows, so no change of it reads as a pinch. Both go from
    // the top of what a number holds to the bottom before they count as a scroll, so the move that commits to it
    // measures a distance of minus infinity: the one place the guard can be reached.
    [Fact]
    public void A_scroll_whose_distance_overflows_turns_nothing_and_goes_on_from_where_the_fingers_are()
    {
        Press(1, -1.7e308, 1.7e308);
        gesture.Down(2, 1.7e308, 1.7e308, ScreenZoom.Fit, at: Ms(10));
        gesture.Move(1, -1.7e308, -1.7e308, Width, Height, Ms(20)).Should().BeNull();
        gesture.IsScrolling.Should().BeFalse("one finger alone is not a scroll yet");
        Sent().Should().BeEmpty();

        gesture.Move(2, 1.7e308, -1.7e308, Width, Height, Ms(30)).Should().BeNull();

        gesture.IsScrolling.Should().BeTrue("both fingers moved the same way");
        Sent().Should().BeEmpty("a distance of minus infinity turns the wheel not at all");

        gesture.Move(1, -1.7e308, -100, Width, Height, Ms(40)).Should().BeNull();
        Sent().Should().HaveCount(ScreenGesture.MaxStepsPerMove, "the scroll goes on from where the fingers were")
            .And.OnlyContain(touch => touch.Kind == ScreenTouchKind.ScrollUp && double.IsFinite(touch.X));
    }

    [Fact]
    public void A_pinch_whose_spread_overflows_keeps_the_view_valid()
    {
        gesture.Down(1, -1.7e308, 150, ScreenZoom.Fit, at: Ms(0));
        gesture.Down(2, 1.7e308, 150, ScreenZoom.Fit, at: Ms(10));

        var shown = gesture.Move(2, 1.6e308, 150, Width, Height, Ms(20));

        shown.Should().Match<ScreenZoom?>(view => view == null
            || (double.IsFinite(view.Value.Scale) && double.IsFinite(view.Value.X) && double.IsFinite(view.Value.Y)));
        gesture.Up(2, at: Ms(30));
        gesture.Up(1, at: Ms(40)).View.Should().Match<ScreenZoom?>(view => view == null
            || (double.IsFinite(view.Value.Scale) && double.IsFinite(view.Value.X) && double.IsFinite(view.Value.Y)));
    }

    [Fact]
    public void A_place_that_is_not_a_number_is_ignored()
    {
        gesture.Down(1, double.NaN, 100, ScreenZoom.Fit, inControl: true, at: Ms(0));
        gesture.IsActive.Should().BeFalse();

        Press(1, 100, 100);
        gesture.Move(1, double.PositiveInfinity, 100, Width, Height, Ms(50)).Should().BeNull();
        gesture.Move(1, 100, double.NaN, Width, Height, Ms(60)).Should().BeNull();
        gesture.Up(1, at: Ms(80));

        Sent().Should().Equal(new ScreenTouch(ScreenTouchKind.Tap, 100, 100));
    }

    [Fact]
    public void One_move_sends_at_most_the_steps_one_scroll_takes_and_a_bigger_jump_is_dropped()
    {
        Press(1, 150, 2400);
        gesture.Down(2, 250, 2400, ScreenZoom.Fit, at: Ms(10));
        gesture.Move(1, 150, 2394, Width, Height, Ms(20));
        gesture.Move(2, 250, 2394, Width, Height, Ms(30));
        gesture.IsScrolling.Should().BeTrue();
        Sent();

        gesture.Move(1, 150, 0, Width, Height, Ms(40));
        var first = Sent();
        gesture.Move(2, 250, 0, Width, Height, Ms(50));
        var second = Sent();

        first.Should().HaveCount(ScreenGesture.MaxStepsPerMove)
            .And.OnlyContain(touch => touch.Kind == ScreenTouchKind.ScrollDown);
        second.Should().HaveCount(ScreenGesture.MaxStepsPerMove);
        gesture.Move(1, 150, 10, Width, Height, Ms(60));
        Sent().Should().BeEmpty("the steps beyond the bound were dropped, not kept for later");
    }

    [Fact]
    public void One_finger_moving_first_does_not_lock_a_scroll_before_the_other_shows_a_pinch()
    {
        Press(1, 150, 150);
        gesture.Down(2, 250, 150, ScreenZoom.Fit, at: Ms(10));

        gesture.Move(1, 150, 120, Width, Height, Ms(20)).Should().BeNull("one finger alone is not a scroll yet");
        gesture.IsScrolling.Should().BeFalse();
        var shown = gesture.Move(2, 250, 190, Width, Height, Ms(30));

        shown.Should().NotBeNull("the second finger showed a pinch");
        gesture.Up(2, at: Ms(40));
        gesture.Up(1, at: Ms(50));
        Sent().Should().BeEmpty("no wheel steps and no click");
    }

    [Fact]
    public void A_cancelled_finger_during_a_pinch_keeps_the_view_and_the_finger_left_moves_it_no_further()
    {
        Press(1, 150, 150);
        gesture.Down(2, 250, 150, ScreenZoom.Fit, at: Ms(10));
        var pinched = gesture.Move(2, 350, 150, Width, Height, Ms(40));

        gesture.Up(2, cancelled: true, at: Ms(50));
        gesture.Move(1, 20, 20, Width, Height, Ms(60)).Should().BeNull();

        gesture.Up(1, at: Ms(70)).Should().Be(new ScreenGestureEnd(false, pinched));
        Sent().Should().BeEmpty();
    }

    [Fact]
    public void A_cancelled_finger_while_watching_stops_the_view_too()
    {
        var enlarged2 = new ScreenZoom(2, -0.5, -0.5);
        gesture.Down(1, 150, 150, enlarged2, at: Ms(0));
        gesture.Down(2, 250, 150, enlarged2, at: Ms(10));
        var pinched = gesture.Move(2, 300, 150, Width, Height, Ms(30));

        gesture.Up(2, cancelled: true, at: Ms(40));
        gesture.Move(1, 20, 20, Width, Height, Ms(50)).Should().BeNull();

        gesture.Up(1, at: Ms(60)).Should().Be(new ScreenGestureEnd(false, pinched));
    }

    [Fact]
    public void Only_the_first_two_fingers_move_the_view_while_watching()
    {
        gesture.Down(1, 150, 150, ScreenZoom.Fit, at: Ms(0));
        gesture.Down(2, 250, 150, ScreenZoom.Fit, at: Ms(10));
        var pinched = gesture.Move(2, 350, 150, Width, Height, Ms(30));

        gesture.Down(3, 50, 250, ScreenZoom.Fit, at: Ms(40));

        gesture.Move(3, 390, 20, Width, Height, Ms(50)).Should().Be(pinched, "a third finger counts for nothing");
    }

    #endregion

    #region Watching

    [Fact]
    public void While_watching_nothing_goes_to_the_sandbox_and_a_tap_is_reported_as_before()
    {
        gesture.Down(1, 100, 100, ScreenZoom.Fit, at: Ms(0));
        gesture.Move(1, 160, 100, Width, Height, Ms(50)).Should().BeNull("a picture at fit does not pan");
        gesture.Up(1, at: Ms(60)).Should().Be(new ScreenGestureEnd(false, ScreenZoom.Fit));

        gesture.Down(1, 100, 100, ScreenZoom.Fit, at: Ms(100));
        gesture.Up(1, at: Ms(900)).Should().Be(new ScreenGestureEnd(true, null), "a long press is still a tap");

        gesture.TakeInput().Should().BeEmpty();
        gesture.IsLongPress(Ms(2000)).Should().BeFalse();
    }

    [Fact]
    public void Control_is_decided_by_the_first_finger_for_the_whole_gesture()
    {
        gesture.Down(1, 100, 100, ScreenZoom.Fit, inControl: false, at: Ms(0));
        gesture.Down(2, 200, 100, ScreenZoom.Fit, inControl: true, at: Ms(10));
        gesture.Move(2, 200, 40, Width, Height, Ms(30));
        gesture.Up(2, at: Ms(40));
        gesture.Up(1, at: Ms(50));

        gesture.TakeInput().Should().BeEmpty();
    }

    [Fact]
    public void A_new_gesture_after_a_drag_starts_clean()
    {
        Press(1, 100, 100);
        gesture.Move(1, 160, 100, Width, Height, Ms(50));
        gesture.Up(1, at: Ms(80));
        Sent();

        Press(1, 300, 200, at: Ms(1000));
        gesture.Up(1, at: Ms(1050));

        Sent().Should().Equal(new ScreenTouch(ScreenTouchKind.Tap, 300, 200));
    }

    #endregion

    #region Helpers

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private void Press(uint id, double x, double y, ScreenZoom? shown = null, TimeSpan at = default) =>
        gesture.Down(id, x, y, shown ?? ScreenZoom.Fit, inControl: true, at: at);

    private IReadOnlyList<ScreenTouch> Sent() => gesture.TakeInput();

    // Two fingers moving together, as a device reports them: each a few pixels at a time, in turn. True when no step
    // changed the view.
    private bool MoveBoth(
        (double X, double Y) firstFrom,
        (double X, double Y) secondFrom,
        (double X, double Y) firstTo,
        (double X, double Y) secondTo,
        TimeSpan at)
    {
        const int Steps = 20;
        var still = true;

        for (var step = 1; step <= Steps; step++)
        {
            var share = (double)step / Steps;
            var first = Between(firstFrom, firstTo, share);
            var second = Between(secondFrom, secondTo, share);
            still &= gesture.Move(1, first.X, first.Y, Width, Height, at) is null;
            still &= gesture.Move(2, second.X, second.Y, Width, Height, at) is null;
        }

        return still;
    }

    private static (double X, double Y) Between((double X, double Y) from, (double X, double Y) to, double share) =>
        (from.X + ((to.X - from.X) * share), from.Y + ((to.Y - from.Y) * share));

    #endregion
}
