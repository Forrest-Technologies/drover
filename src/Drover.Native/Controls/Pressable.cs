namespace Drover.Native.Controls;

/// <summary>
/// What every pressable control of Drover shares: it knows whether the pointer is over it, whether it is held down,
/// and whether the keyboard moved the focus to it, and it repaints itself from tokens when any of those changes or
/// the theme does. It is a real button to the platform: a tab stop, pressed with Space and Enter, named and
/// announced as a button.
/// </summary>
public abstract class Pressable : Microsoft.UI.Xaml.Controls.Button
{
    #region Constructors

    protected Pressable(string template)
    {
        Template = Styles.Template(template);
        FontFamily = Tokens.FontUi;
        FontSize = Tokens.Number("DvText14");
        UseSystemFocusVisuals = false;
        MinHeight = Targets.Minimum;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;

        PointerEntered += (_, _) => Set(() => IsHovered = true);
        PointerExited += (_, _) => Set(() => (IsHovered, IsHeld) = (false, false));
        PointerCanceled += (_, _) => Set(() => (IsHovered, IsHeld) = (false, false));
        PointerCaptureLost += (_, _) => Set(() => IsHeld = false);
        GotFocus += (_, _) => Set(() => HasKeyboardFocus = FocusState is FocusState.Keyboard);
        LostFocus += (_, _) => Set(() => HasKeyboardFocus = false);
        IsEnabledChanged += (_, _) => Repaint();
        this.WhenShownOrRethemed(Repaint);
    }

    #endregion

    #region Properties

    protected bool IsHovered { get; private set; }

    protected bool IsHeld { get; private set; }

    /// <summary>True while the focus is here and the keyboard put it here: the only time the ring shows.</summary>
    protected bool HasKeyboardFocus { get; private set; }

    /// <summary>The frame named in the template: a <see cref="Facet"/> or a <see cref="Border"/>.</summary>
    protected FrameworkElement? Frame { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>Moves the focus here, for when this control takes the place of the one a person just used.</summary>
    public void TakeFocus() => Focus(FocusState.Keyboard);

    #endregion

    #region Private Methods

    /// <summary>Sets the frame's and the text's colors for the state the control is in.</summary>
    protected abstract void Paint();

    protected void Repaint()
    {
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
            IsEnabled ? Microsoft.UI.Input.InputSystemCursorShape.Hand : Microsoft.UI.Input.InputSystemCursorShape.UniversalNo);

        if (Frame is not null)
        {
            Paint();
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Frame = GetTemplateChild(nameof(Frame)) as FrameworkElement;
        Repaint();
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs args)
    {
        base.OnPointerPressed(args);
        Set(() => IsHeld = true);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs args)
    {
        base.OnPointerReleased(args);
        Set(() => IsHeld = false);
    }

    private void Set(Action change)
    {
        change();
        Repaint();
    }

    #endregion
}
