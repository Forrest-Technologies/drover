namespace Drover.Native.Bots;

/// <summary>
/// The picture of a sandbox's screen, drawn in a frame that keeps the screen's shape. Enlarged, the picture is moved
/// inside the frame; the frame never grows. A pinch enlarges it and a drag moves it, shown at once. While the person
/// drives the screen it is a button: a click or a tap clicks the sandbox pixel under it, the arrow keys move a pointer
/// over it, and Enter or Space clicks there. A finger follows the touch model of <see cref="ScreenGesture"/>: a tap
/// clicks, a long press right-clicks, a press and move drags, two fingers scroll or zoom; the picture keeps the finger
/// so the page does not scroll under it. While the person only looks, an enlarged picture takes the focus so the
/// arrow keys can move the view. Tab is never taken, so the keyboard enters and leaves the picture as it does any
/// other control.
/// </summary>
internal sealed class SandboxPicture : UserControl
{
    #region Private Fields

    private readonly SandboxScreenViewModel model;

    private readonly Border frame = new();

    private readonly Grid box = new();

    private readonly PictureLayer layer = new();

    private readonly CompositeTransform place = new();

    private readonly ScreenGesture gesture = new();

    private Storyboard? settling;

    private ScreenZoom placed = ScreenZoom.Fit;

    private (double X, double Y) pressedAt;

    // The time of each pointer event, read by the gesture, which keeps no clock of its own.
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    // Waits once, while a still finger rests, to say that lifting it now right-clicks. Nothing repeats. Each wait is
    // numbered; a finger that lifts or a second finger moves the number on, so an older wait says nothing.
    private int rest;

    private bool resting;

    private bool pressedByMouse;

    // Whether the gesture under way began while the person drove the screen.
    private bool gestureInControl;

    #endregion

    #region Constructors

    public SandboxPicture(SandboxScreenViewModel model)
    {
        this.model = model;
        layer.RenderTransform = place;
        box.Children.Add(layer);
        frame.Child = box;
        frame.BorderThickness = new(Tokens.Number("DvBorder"));
        frame.CornerRadius = new(Tokens.Number("DvRadiusControl"));
        Content = frame;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        box.SizeChanged += (_, _) => Fit();
        PointerPressed += Pressed;
        PointerMoved += Moved;
        PointerReleased += (_, pointer) => Lifted(pointer, cancelled: false);
        PointerCanceled += (_, pointer) => Lifted(pointer, cancelled: true);
        PointerCaptureLost += (_, pointer) => Lifted(pointer, cancelled: true);
        KeyDown += Typed;
        LostFocus += (_, _) => LetGo();
        Unloaded += (_, _) => LetGo();
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    /// <summary>True while a finger or the mouse is down on the picture.</summary>
    public bool IsPressed => gesture.IsActive;

    /// <summary>Raised when the last finger leaves the picture.</summary>
    public event Action? Released;

    private bool Enlarged => !model.Zoom.IsFit;

    private bool KeepsFinger => Enlarged || model.Control;

    private TimeSpan Now => clock.Elapsed;

    private static bool ShiftIsDown =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    #endregion

    #region Public Methods

    /// <summary>Draws the picture, the view, and the pointer as the model has them now.</summary>
    public void Show()
    {
        var focusable = model.Control || Enlarged;

        layer.Show(model.Picture, model.Control ? model.PointerAt : null, model.PictureWidth, model.PictureHeight, model.Zoom.Scale);
        frame.Background = Tokens.Brush(this, "DvBg2");
        frame.BorderBrush = Tokens.Brush(this, model.Control ? "DvEmber" : "DvLine");
        IsTabStop = focusable;

        // Enlarged, a finger on the picture moves the picture, not the page; in control it drives the sandbox. Either
        // way the picture keeps the finger. Looking at a picture at fit, the finger scrolls the page as anywhere else.
        ManipulationMode = KeepsFinger ? ManipulationModes.None : ManipulationModes.System;
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
            model.Control ? Microsoft.UI.Input.InputSystemCursorShape.Cross
            : Enlarged ? Microsoft.UI.Input.InputSystemCursorShape.SizeAll
            : Microsoft.UI.Input.InputSystemCursorShape.Arrow);

        Describe();
        ShowRing();
        InvalidateMeasure();

        // Control ended while a finger was down: the whole gesture ends (a held drag is let go of), so a press or a
        // scroll can never carry on when control comes back. A gesture that began while only looking is left alone.
        if (!model.Control && gestureInControl && gesture.IsActive)
        {
            LetGo();
        }

        if (!gesture.IsActive && model.Zoom != placed)
        {
            Place(model.Zoom, settle: true);
        }
    }

    #endregion

    #region Layout

    // The frame fills the width it has and keeps the screen's shape, so nothing jumps when the picture arrives.
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? model.PictureWidth : availableSize.Width;
        var size = new Size(width, width * model.PictureHeight / model.PictureWidth);
        base.MeasureOverride(size);
        return size;
    }

    private void Fit()
    {
        box.Clip = new RectangleGeometry { Rect = new(0, 0, box.ActualWidth, box.ActualHeight) };
        Place(gesture.IsActive ? placed : model.Zoom, settle: false);
    }

    // The view is a share of the frame, so the same view holds at any size the frame is shown at.
    private void Place(ScreenZoom view, bool settle)
    {
        var (scale, x, y) = (view.Scale, view.X * box.ActualWidth, view.Y * box.ActualHeight);
        var time = Motion.Duration("DvDur2");
        placed = view;
        settling?.Stop();
        settling = null;

        if (!settle || time == TimeSpan.Zero)
        {
            (place.ScaleX, place.ScaleY, place.TranslateX, place.TranslateY) = (scale, scale, x, y);
            return;
        }

        var story = new Storyboard();
        story.Children.Add(Glide(nameof(CompositeTransform.ScaleX), place.ScaleX, scale, time));
        story.Children.Add(Glide(nameof(CompositeTransform.ScaleY), place.ScaleY, scale, time));
        story.Children.Add(Glide(nameof(CompositeTransform.TranslateX), place.TranslateX, x, time));
        story.Children.Add(Glide(nameof(CompositeTransform.TranslateY), place.TranslateY, y, time));
        story.Completed += (_, _) =>
        {
            if (settling == story)
            {
                settling = null;
                (place.ScaleX, place.ScaleY, place.TranslateX, place.TranslateY) = (scale, scale, x, y);
            }
        };
        settling = story;
        story.Begin();
    }

    private DoubleAnimation Glide(string property, double from, double to, TimeSpan time)
    {
        var glide = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = time,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(glide, place);
        Storyboard.SetTargetProperty(glide, property);
        return glide;
    }

    #endregion

    #region Pointer

    private void Pressed(object sender, PointerRoutedEventArgs pointer)
    {
        var point = pointer.GetCurrentPoint(box);
        var isMouse = pointer.Pointer.PointerDeviceType is Microsoft.UI.Input.PointerDeviceType.Mouse;
        if (isMouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // The picture keeps the finger only when it holds it. A finger the picture could not capture is left to the
        // page and starts nothing here, so the gesture never waits for a lift it may not be told of. A finger on a
        // picture at fit, only looked at, may be scrolling the page, which the system decides.
        var keeps = isMouse || KeepsFinger;
        if (keeps && !CapturePointer(pointer.Pointer))
        {
            return;
        }

        if (gesture.IsActive)
        {
            // A second finger is never a long press.
            CalmLongPress();
        }
        else
        {
            (pressedByMouse, gestureInControl) = (isMouse, model.Control);
        }

        pressedAt = (point.Position.X, point.Position.Y);
        gesture.Down(pointer.Pointer.PointerId, point.Position.X, point.Position.Y, placed, model.Control, Now);
        SendTouches();
        WatchForLongPress();
        pointer.Handled = keeps;

        if (IsTabStop)
        {
            Focus(FocusState.Pointer);
        }
    }

    private void Moved(object sender, PointerRoutedEventArgs pointer)
    {
        var point = pointer.GetCurrentPoint(box).Position;
        var view = gesture.Move(pointer.Pointer.PointerId, point.X, point.Y, box.ActualWidth, box.ActualHeight, Now);
        if (gesture.IsActive && KeepsFinger)
        {
            pointer.Handled = true;
        }

        SendTouches();
        if (view is { } shown)
        {
            pointer.Handled = true;
            Place(shown, settle: false);
            var pointerAt = model.Control ? model.PointerAt : ((int X, int Y)?)null;
            layer.Show(model.Picture, pointerAt, model.PictureWidth, model.PictureHeight, shown.Scale);
        }
    }

    private async void Lifted(PointerRoutedEventArgs pointer, bool cancelled)
    {
        var wasActive = gesture.IsActive;
        var end = gesture.Up(pointer.Pointer.PointerId, cancelled, Now);
        if (!gesture.IsActive)
        {
            CalmLongPress();
        }

        SendTouches();
        if (!cancelled)
        {
            ReleasePointerCapture(pointer.Pointer);
        }

        if (wasActive && !gesture.IsActive)
        {
            Released?.Invoke();
        }

        if (end.View is { } view)
        {
            model.ViewChanged(view.Scale, view.X, view.Y);
        }
        else if (end.Tapped && model.Control)
        {
            // Control came on while the finger was down, so the gesture was a look: the tap still clicks there.
            await model.Tap(pressedAt.X, pressedAt.Y, box.ActualWidth, box.ActualHeight);
        }
    }

    // What the gesture sends goes to the model at once, measured on the frame as it is now. A mouse that is held still
    // clicks, as a mouse does; only a finger right-clicks by resting.
    private void SendTouches()
    {
        var touches = gesture.TakeInput();
        if (touches.Count == 0)
        {
            return;
        }

        List<ScreenTouch> sent =
        [
            .. touches.Select(touch => pressedByMouse && touch.Kind is ScreenTouchKind.LongPress
                ? touch with { Kind = ScreenTouchKind.Tap }
                : touch),
        ];
        model.Touch(sent, box.ActualWidth, box.ActualHeight);
    }

    // The picture lost the focus, went away, or control ended: a held drag is let go of where the finger last was.
    private void LetGo()
    {
        CalmLongPress();
        if (gesture.Release())
        {
            SendTouches();
        }

        ReleasePointerCaptures();
    }

    private void WatchForLongPress()
    {
        if (model.Control && !pressedByMouse && gesture.IsActive && !resting)
        {
            resting = true;
            _ = SayLongPressAfterRest(++rest);
        }
    }

    private async Task SayLongPressAfterRest(int wait)
    {
        await Task.Delay(ScreenGesture.LongPressTime);
        if (resting && wait == rest && gesture.IsLongPress(Now))
        {
            model.LongPressReady(true);
        }
    }

    private void CalmLongPress()
    {
        resting = false;
        rest++;
        if (model.TouchWords == SandboxScreenViewModel.LongPressWords)
        {
            model.LongPressReady(false);
        }
    }

    #endregion

    #region Keyboard

    private async void Typed(object sender, KeyRoutedEventArgs key)
    {
        var name = key.Key switch
        {
            VirtualKey.Left => "ArrowLeft",
            VirtualKey.Right => "ArrowRight",
            VirtualKey.Up => "ArrowUp",
            VirtualKey.Down => "ArrowDown",
            _ => null,
        };

        if (name is not null)
        {
            // The arrow keys over the picture never scroll the page.
            key.Handled = true;
            _ = model.Control ? model.MovePointer(name, ShiftIsDown) : model.PanView(name, ShiftIsDown);
        }
        else if (model.Control && key.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            key.Handled = true;
            await model.ClickAtPointer();
        }
    }

    // The ember ring, outside the frame with a gap, only when the keyboard moved the focus here.
    private void ShowRing()
    {
        var width = Tokens.Number("DvFocusWidth");
        var offset = Tokens.Number("DvFocusOffset") + width;

        // The platform draws the ring only when the keyboard moved the focus, which is the rule.
        UseSystemFocusVisuals = true;
        FocusVisualPrimaryBrush = Tokens.Brush(this, "DvEmber");
        FocusVisualPrimaryThickness = new(width);
        FocusVisualSecondaryThickness = new(0);
        FocusVisualMargin = new(-offset);
        CornerRadius = new(Tokens.Number("DvRadiusControl") + offset);
    }

    #endregion

    #region Names

    // In control the picture is a button, described by where the pointer is. Enlarged and only looked at, it is a
    // group that takes the focus, and the picture inside keeps its own name.
    private void Describe()
    {
        AutomationProperties.SetName(
            this, model.Control ? model.ControlPictureName : Enlarged ? SandboxScreenViewModel.EnlargedName : model.ViewPictureName);
        AutomationProperties.SetHelpText(this, model.Control ? model.PointerWords : string.Empty);
        AutomationProperties.SetName(layer, model.ViewPictureName);
        AutomationProperties.SetAccessibilityView(layer, !model.Control && Enlarged ? AccessibilityView.Content : AccessibilityView.Raw);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PicturePeer(this, model);

    #endregion
}
