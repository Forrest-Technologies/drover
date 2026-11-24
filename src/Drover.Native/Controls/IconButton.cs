namespace Drover.Native.Controls;

/// <summary>
/// A button that shows only an icon. What it does is said in <see cref="Label"/>, which is its accessible name and
/// its tooltip, so it is required: an icon alone has no name. A button that turns something on and off, such as
/// wrapping lines, sets <see cref="Pressed"/>: it is then a toggle to assistive technology, its state is read out, and
/// while on it stands on the selected surface, so its state is never told by the icon alone.
/// </summary>
public sealed class IconButton : PlainButton
{
    #region Private Fields

    private readonly Icon icon = new() { Size = 20 };

    #endregion

    #region Constructors

    public IconButton()
    {
        Content = icon;
        Padding = new(0);
        Ink = "DvText2";
        Width = Height = Targets.Minimum;
        Loaded += (_, _) => RequireLabel();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty IconProperty =
        Dp.Register<IconButton, IconName>(nameof(Icon), IconName.Info, button => button.icon.NameOfIcon = button.Icon);

    public static readonly DependencyProperty LabelProperty =
        Dp.Register<IconButton, string>(nameof(Label), string.Empty, button => button.ApplyLabel());

    public static readonly DependencyProperty PressedProperty =
        Dp.Register<IconButton, bool?>(nameof(Pressed), null, button => button.ApplyPressed());

    public IconName Icon
    {
        get => (IconName)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>What the control does, in words. It is the accessible name, so it is required.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>True while a toggle is on, false while it is off, and null for a button that only acts.</summary>
    public bool? Pressed
    {
        get => (bool?)GetValue(PressedProperty);
        set => SetValue(PressedProperty, value);
    }

    #endregion

    #region Private Methods

    protected override void Painted() => icon.Foreground = CurrentInk;

    protected override AutomationPeer OnCreateAutomationPeer() => new IconButtonPeer(this);

    private void ApplyPressed()
    {
        Fill = Pressed is true ? "DvBg3" : null;

        // Before its template is applied the button has no frame to paint; it paints when it gets one.
        if (Frame is not null)
        {
            Paint();
        }

        (FrameworkElementAutomationPeer.FromElement(this) as IconButtonPeer)?.TellToggled();
    }

    private void ApplyLabel()
    {
        AutomationProperties.SetName(this, Label);
        ToolTipService.SetToolTip(this, Label);
    }

    private void RequireLabel()
    {
        if (string.IsNullOrWhiteSpace(Label))
        {
            throw new InvalidOperationException($"{nameof(IconButton)} needs a {nameof(Label)}: an icon alone has no name.");
        }
    }

    #endregion

    #region Nested Types

    // A plain button to assistive technology, or a toggle with its state where the button has one.
    private sealed class IconButtonPeer(IconButton owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        #region Properties

        public ToggleState ToggleState => owner.Pressed is true ? ToggleState.On : ToggleState.Off;

        #endregion

        #region Interface Implementations

        public void Toggle() => ((IInvokeProvider)this).Invoke();

        #endregion

        #region Public Methods

        public void TellToggled() =>
            RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, owner.Pressed is true ? ToggleState.Off : ToggleState.On, ToggleState);

        #endregion

        #region Private Methods

        protected override object? GetPatternCore(PatternInterface patternInterface) =>
            patternInterface is PatternInterface.Toggle && owner.Pressed is not null ? this : base.GetPatternCore(patternInterface);

        #endregion
    }

    #endregion
}
