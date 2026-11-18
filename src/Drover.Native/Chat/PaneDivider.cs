namespace Drover.Native.Chat;

/// <summary>A column boundary that moves with the pointer, or with the arrow keys while it has focus.</summary>
internal sealed class PaneDivider : UserControl
{
    #region Private Fields

    private readonly Grid grip = new();

    private readonly Border line = new()
    {
        Width = Tokens.Number("DvBorder"),
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private readonly Grid workspace;

    private readonly Func<double> width;

    private readonly Action<double> resize;

    private readonly int direction;

    private uint? pointerId;

    private double startX;

    private double startWidth;

    private bool hovered;

    #endregion

    #region Constructors

    public PaneDivider(Grid workspace, string label, Func<double> width, Action<double> resize, bool growsLeft)
    {
        this.workspace = workspace;
        this.width = width;
        this.resize = resize;
        direction = growsLeft ? -1 : 1;
        grip.Children.Add(line);
        Content = grip;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = true;
        ManipulationMode = ManipulationModes.None;
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
            Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
        AutomationProperties.SetName(this, label);
        AutomationProperties.SetHelpText(this, "Use the left and right arrow keys to move the divider.");

        PointerEntered += (_, _) => SetHover(true);
        PointerExited += (_, _) => SetHover(false);
        PointerPressed += Pressed;
        PointerMoved += Moved;
        PointerReleased += (_, args) => ReleasePointerCapture(args.Pointer);
        PointerCanceled += EndDrag;
        PointerCaptureLost += EndDrag;
        KeyDown += Typed;
        GotFocus += (_, _) => Paint();
        LostFocus += (_, _) => Paint();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Thumb);

    private void Pressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(workspace);
        var mouse = args.Pointer.PointerDeviceType is Microsoft.UI.Input.PointerDeviceType.Mouse;
        if (pointerId is not null || (mouse && !point.Properties.IsLeftButtonPressed) || !CapturePointer(args.Pointer))
        {
            return;
        }

        (pointerId, startX, startWidth) = (args.Pointer.PointerId, point.Position.X, width());
        Focus(FocusState.Pointer);
        args.Handled = true;
        Paint();
    }

    private void Moved(object sender, PointerRoutedEventArgs args)
    {
        if (pointerId == args.Pointer.PointerId)
        {
            var offset = args.GetCurrentPoint(workspace).Position.X - startX;
            resize(startWidth + direction * offset);
            args.Handled = true;
        }
    }

    private void Typed(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Left or VirtualKey.Right)
        {
            var step = Tokens.Number("DvSpace4") * (args.Key is VirtualKey.Left ? -1 : 1);
            resize(width() + direction * step);
            args.Handled = true;
        }
    }

    private void EndDrag(object sender, PointerRoutedEventArgs args)
    {
        if (pointerId == args.Pointer.PointerId)
        {
            pointerId = null;
            Paint();
        }
    }

    private void SetHover(bool value)
    {
        hovered = value;
        Paint();
    }

    private void Paint()
    {
        var focused = FocusState is FocusState.Keyboard;
        var active = hovered || pointerId is not null;
        grip.Background = Tokens.Brush(this, active || focused ? "DvBg3" : "DvBg1");
        line.Background = Tokens.Brush(this, focused ? "DvEmber" : active ? "DvText2" : "DvLine");
    }

    #endregion
}
