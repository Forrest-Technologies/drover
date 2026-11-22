namespace Drover.Presentation.ViewModels.Bots;

/// <summary>
/// The fingers or the mouse on a sandbox's picture, from the first one down to the last one up. Places are measured
/// from the top left corner of the picture's frame, and times from any fixed moment the caller keeps, so the model
/// reads no clock.
/// <para>
/// Watching (control off): a pinch enlarges the picture and a drag moves it, shown at once and told to the screen
/// when the gesture ends; a press that neither moved nor pinched is a tap. Nothing goes to the sandbox.
/// </para>
/// <para>
/// In control: a tap is a click and a long press is a right click, both when the finger lifts. A press that moves
/// past <see cref="TapSlop"/> drags with the left button held: press, moves, release. On an enlarged picture a quick
/// move pans the view instead, and a move after <see cref="HoldTime"/> drags. Two fingers moving together turn the
/// scroll wheel; two fingers whose spread changes pinch, which zooms and pans the view. A second finger during a drag
/// releases the button and ends the gesture. Nothing clicks after a drag, a scroll or a pinch. What goes to the
/// sandbox is collected and handed over by <see cref="TakeInput"/>.
/// </para>
/// <para>
/// Only the first two fingers down count: their centre and their spread move the view and turn the wheel. A third
/// finger in control ends what two were doing. A finger the system cancels while others stay ends the gesture's
/// effects too: nothing more is sent and the view moves no further.
/// </para>
/// </summary>
public sealed class ScreenGesture
{
    #region Private Fields

    private readonly Dictionary<uint, (double X, double Y)> pointers = [];

    // The fingers in the order they went down. The first two are the ones that count.
    private readonly List<uint> order = [];

    // Where each counted finger was when the gesture last changed shape, so a scroll can see both fingers move.
    private readonly Dictionary<uint, (double X, double Y)> starts = [];

    // A finger was cancelled while others stayed: nothing the rest do changes the view or reaches the sandbox.
    private bool frozen;

    private readonly List<ScreenTouch> input = [];

    private bool moved;

    private bool pinched;

    private bool control;

    private TouchPhase phase;

    private ScreenZoom view = ScreenZoom.Fit;

    private ScreenZoom from = ScreenZoom.Fit;

    private (double X, double Y, double Spread) start;

    private TimeSpan downAt;

    private (double X, double Y) pressedAt;

    private (double X, double Y) last;

    private double scrolled;

    #endregion

    #region Properties

    /// <summary>How far a pointer may travel and still be a tap.</summary>
    public const double TapSlop = 10;

    /// <summary>How far two fingers move up or down for one turn of the scroll wheel.</summary>
    public const double ScrollStep = 24;

    /// <summary>How much two fingers' spread must change, as a share of where it started, to be a pinch.</summary>
    public const double PinchShare = 0.12;

    /// <summary>The most wheel steps one move of the fingers sends, the most the server takes in one scroll.</summary>
    public const int MaxStepsPerMove = 20;

    /// <summary>
    /// How long a finger rests before a move drags instead of panning an enlarged picture. Long enough that a quick
    /// swipe still pans, short enough that a deliberate press and drag never waits.
    /// </summary>
    public static TimeSpan HoldTime { get; } = TimeSpan.FromMilliseconds(300);

    /// <summary>How long a finger rests without moving for its lift to be a right click instead of a click.</summary>
    public static TimeSpan LongPressTime { get; } = TimeSpan.FromMilliseconds(600);

    /// <summary>True while at least one pointer is down on the picture.</summary>
    public bool IsActive => pointers.Count > 0;

    /// <summary>True while the left button is held down in the sandbox by a drag.</summary>
    public bool IsDragging => phase is TouchPhase.Dragging;

    /// <summary>True while two fingers turn the scroll wheel.</summary>
    public bool IsScrolling => phase is TouchPhase.Scrolling;

    #endregion

    #region Public Methods

    /// <summary>
    /// A pointer went down on the picture, which shows <paramref name="shown"/> when it is the first one.
    /// <paramref name="inControl"/> says, for the first pointer, whether what it does goes to the sandbox.
    /// </summary>
    public void Down(uint id, double x, double y, ScreenZoom shown, bool inControl = false, TimeSpan at = default)
    {
        // A second down for a finger that is already down changes nothing, nor does a place that is not a number.
        if (pointers.ContainsKey(id) || !double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        if (pointers.Count == 0)
        {
            (moved, pinched, view, control, frozen) = (false, false, shown, inControl, false);
            (phase, downAt, pressedAt, last, scrolled) = (TouchPhase.Pressed, at, (x, y), (x, y), 0);
        }

        pointers[id] = (x, y);
        order.Add(id);
        pinched |= pointers.Count > 1;

        if (control && pointers.Count > 1)
        {
            SecondFinger();
        }

        Rebase();
    }

    /// <summary>
    /// A pointer moved over a frame <paramref name="width"/> by <paramref name="height"/>. The view to show at once,
    /// or null when the view does not change: the pointer is not one of this gesture's, it has not left the place it
    /// went down, it drags a picture that is not enlarged, or it drives the sandbox.
    /// </summary>
    public ScreenZoom? Move(uint id, double x, double y, double width, double height, TimeSpan at = default)
    {
        if (!pointers.ContainsKey(id) || width <= 0 || height <= 0 || !double.IsFinite(x) || !double.IsFinite(y))
        {
            return null;
        }

        pointers[id] = (x, y);
        if (frozen)
        {
            return null;
        }

        return control ? MoveInControl(x, y, width, height, at) : MoveWatching(width, height);
    }

    /// <summary>
    /// A pointer went up, or was taken away by the system. When it was the last one, says what the gesture was: a tap,
    /// or a view to keep. In control a tap is not reported here: it is a click in <see cref="TakeInput"/>.
    /// </summary>
    public ScreenGestureEnd Up(uint id, bool cancelled = false, TimeSpan at = default)
    {
        if (!pointers.Remove(id))
        {
            return default;
        }

        order.Remove(id);
        if (pointers.Count > 0)
        {
            if (cancelled)
            {
                Freeze();
            }

            FingerLifted();
            Rebase();
            return default;
        }

        return control ? EndInControl(cancelled, at) : EndWatching(cancelled);
    }

    /// <summary>
    /// True when the finger has rested long enough that lifting it now right-clicks, so the screen can say so before
    /// the person lifts.
    /// </summary>
    public bool IsLongPress(TimeSpan at) =>
        control && phase is TouchPhase.Pressed && pointers.Count == 1 && at - downAt >= LongPressTime;

    /// <summary>
    /// Ends the gesture now: the screen lost the focus, control ended, or the picture went away. A button held by a
    /// drag is released where the finger last was. True when a button was released.
    /// </summary>
    public bool Release()
    {
        var released = EndDrag();
        pointers.Clear();
        order.Clear();
        phase = TouchPhase.None;
        return released;
    }

    /// <summary>
    /// What the gesture sends to the sandbox since it was last asked, in order, at places on the frame. A
    /// <see cref="ScreenTouchKind.Press"/> may be handed over before its release: the gesture then always hands over
    /// exactly one <see cref="ScreenTouchKind.Release"/> for it, when the finger lifts, is cancelled, or is joined by
    /// another, or when the caller calls <see cref="Release"/>. A caller that stops asking calls <see cref="Release"/>
    /// first and takes what it gives.
    /// </summary>
    public IReadOnlyList<ScreenTouch> TakeInput()
    {
        List<ScreenTouch> taken = [.. input];
        input.Clear();
        return taken;
    }

    #endregion

    #region Watching

    private ScreenZoom? MoveWatching(double width, double height)
    {
        var now = Centre();
        var pinching = pointers.Count > 1;

        if (!moved && !pinching && Travel(now) < TapSlop)
        {
            return null;
        }

        moved = true;
        if (!pinching && from.Scale <= 1)
        {
            return null;
        }

        return Pan(now, pinching, width, height);
    }

    private ScreenGestureEnd EndWatching(bool cancelled) =>
        moved ? new(false, view) : new(!cancelled && !pinched, null);

    #endregion

    #region Control

    private ScreenZoom? MoveInControl(double x, double y, double width, double height, TimeSpan at)
    {
        var now = Centre();

        switch (phase)
        {
            case TouchPhase.Pressed when Travel(now) >= TapSlop:
                if (from.Scale > 1 && at - downAt < HoldTime)
                {
                    phase = TouchPhase.Panning;
                    return Pan(now, pinching: false, width, height);
                }

                phase = TouchPhase.Dragging;
                input.Add(new(ScreenTouchKind.Press, pressedAt.X, pressedAt.Y));
                return Drag(x, y);
            case TouchPhase.Panning:
                return Pan(now, pinching: false, width, height);
            case TouchPhase.Dragging:
                return Drag(x, y);
            case TouchPhase.TwoFingers:
                return TwoFingers(now, width, height);
            case TouchPhase.Scrolling:
                return Scroll(now);
            case TouchPhase.Pinching:
                return Pan(now, pinching: pointers.Count > 1, width, height);
            default:
                return null;
        }
    }

    private ScreenGestureEnd EndInControl(bool cancelled, TimeSpan at)
    {
        var ended = phase;
        phase = TouchPhase.None;

        switch (ended)
        {
            case TouchPhase.Dragging:
                input.Add(new(ScreenTouchKind.Release, last.X, last.Y));
                return default;
            case TouchPhase.Pressed when !cancelled:
                var kind = at - downAt >= LongPressTime ? ScreenTouchKind.LongPress : ScreenTouchKind.Tap;
                input.Add(new(kind, pressedAt.X, pressedAt.Y));
                return default;
            case TouchPhase.Panning or TouchPhase.Pinching:
            case TouchPhase.Spent when moved:
                return new(false, view);
            default:
                return default;
        }
    }

    // A second finger turns a press into two fingers, and ends a drag: the button is released where the first finger
    // was, and nothing else happens until every finger is up, so the button is never held by a finger the person
    // did not mean to drag with. A third finger ends whatever two were doing: the place between the fingers jumps
    // when it lands, and that jump must never turn the wheel or move the view.
    private void SecondFinger()
    {
        if (EndDrag() || pointers.Count > 2)
        {
            phase = TouchPhase.Spent;
        }
        else if (phase is TouchPhase.Pressed or TouchPhase.Panning)
        {
            phase = TouchPhase.TwoFingers;
        }
    }

    // A finger that lifts while others stay ends a scroll; a pinch goes on with the fingers left, as when watching.
    private void FingerLifted()
    {
        if (control && phase is TouchPhase.TwoFingers or TouchPhase.Scrolling)
        {
            phase = TouchPhase.Spent;
        }
    }

    // Two fingers are a scroll until their spread changes enough to be a pinch; once either, they stay it.
    private ScreenZoom? TwoFingers((double X, double Y, double Spread) now, double width, double height)
    {
        if (Math.Abs((now.Spread / start.Spread) - 1) >= PinchShare)
        {
            phase = TouchPhase.Pinching;
            return Pan(now, pinching: true, width, height);
        }

        // A scroll needs both fingers to have moved the same way: one finger alone moving first may be the start of a
        // pinch, and the other finger has not had its say yet.
        if (!BothMovedTogether())
        {
            return null;
        }

        phase = TouchPhase.Scrolling;
        last = (now.X, start.Y);
        return Scroll(now);
    }

    // Fingers moving up bring what is below into view, as a page does under a finger: the wheel turns down.
    private ScreenZoom? Scroll((double X, double Y, double Spread) now)
    {
        scrolled += now.Y - last.Y;
        last = (now.X, now.Y);

        // One move sends at most the steps the server takes in one scroll; a jump beyond that is dropped, not queued.
        var due = Math.Truncate(scrolled / ScrollStep);
        var steps = (int)Math.Clamp(due, -MaxStepsPerMove, MaxStepsPerMove);
        var kind = steps < 0 ? ScreenTouchKind.ScrollDown : ScreenTouchKind.ScrollUp;
        for (var step = 0; step < Math.Abs(steps); step++)
        {
            input.Add(new(kind, now.X, now.Y));
        }

        scrolled = steps == due ? scrolled - (steps * ScrollStep) : 0;
        return null;
    }

    private bool BothMovedTogether()
    {
        if (order.Count < 2
            || !starts.TryGetValue(order[0], out var firstFrom)
            || !starts.TryGetValue(order[1], out var secondFrom))
        {
            return false;
        }

        var first = pointers[order[0]].Y - firstFrom.Y;
        var second = pointers[order[1]].Y - secondFrom.Y;
        var enough = TapSlop / 2;

        return Math.Sign(first) == Math.Sign(second) && Math.Abs(first) >= enough && Math.Abs(second) >= enough;
    }

    // A cancelled finger while others stay: a held drag is let go of, and the rest of the gesture does nothing.
    private void Freeze()
    {
        frozen = true;
        if (control)
        {
            EndDrag();
            phase = TouchPhase.Spent;
        }
    }

    private ScreenZoom? Drag(double x, double y)
    {
        if ((x, y) != last)
        {
            input.Add(new(ScreenTouchKind.Drag, x, y));
            last = (x, y);
        }

        return null;
    }

    private bool EndDrag()
    {
        if (phase is not TouchPhase.Dragging)
        {
            return false;
        }

        input.Add(new(ScreenTouchKind.Release, last.X, last.Y));
        phase = TouchPhase.None;
        return true;
    }

    #endregion

    #region View

    private ScreenZoom Pan((double X, double Y, double Spread) now, bool pinching, double width, double height)
    {
        moved = true;
        var stretch = pinching ? now.Spread / start.Spread : 1;
        var scale = Math.Clamp(from.Scale * stretch, 1, ScreenZoom.MaxScale);
        var (fromX, fromY) = (start.X / width, start.Y / height);
        var (toX, toY) = (now.X / width, now.Y / height);
        view = new(
            scale,
            Math.Clamp(toX - ((fromX - from.X) / from.Scale * scale), 1 - scale, 0),
            Math.Clamp(toY - ((fromY - from.Y) / from.Scale * scale), 1 - scale, 0));
        return view;
    }

    private void Rebase()
    {
        from = view;
        start = Centre();
        starts.Clear();
        foreach (var id in order.Take(2))
        {
            starts[id] = pointers[id];
        }
    }

    private double Travel((double X, double Y, double Spread) now) =>
        Math.Sqrt(Math.Pow(now.X - start.X, 2) + Math.Pow(now.Y - start.Y, 2));

    // The place between the first two fingers down, and how far apart they are. Fingers after them do not count.
    private (double X, double Y, double Spread) Centre()
    {
        List<(double X, double Y)> points = [.. order.Take(2).Select(id => pointers[id])];
        var spread = points.Count > 1
            ? Math.Sqrt(Math.Pow(points[0].X - points[1].X, 2) + Math.Pow(points[0].Y - points[1].Y, 2))
            : 1;
        return (points.Average(point => point.X), points.Average(point => point.Y), spread > 0 ? spread : 1);
    }

    #endregion

    #region Phases

    private enum TouchPhase
    {
        None,
        Pressed,
        Panning,
        Dragging,
        TwoFingers,
        Scrolling,
        Pinching,
        Spent,
    }

    #endregion
}

/// <summary>How a gesture on the picture ended: with a tap, with a view to keep, or with neither.</summary>
public readonly record struct ScreenGestureEnd(bool Tapped, ScreenZoom? View);

/// <summary>One thing a gesture in control sends to the sandbox, at a place on the picture's frame.</summary>
public readonly record struct ScreenTouch(ScreenTouchKind Kind, double X, double Y);

/// <summary>What a gesture in control sends to the sandbox.</summary>
public enum ScreenTouchKind
{
    /// <summary>A click, of the type the screen's click choice names.</summary>
    Tap,

    /// <summary>A right click.</summary>
    LongPress,

    /// <summary>The left button goes down: a drag starts where the finger went down.</summary>
    Press,

    /// <summary>The pointer moves with the left button held.</summary>
    Drag,

    /// <summary>The left button comes up where the pointer last was.</summary>
    Release,

    /// <summary>One turn of the scroll wheel up.</summary>
    ScrollUp,

    /// <summary>One turn of the scroll wheel down.</summary>
    ScrollDown,
}
