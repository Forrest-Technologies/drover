namespace Drover.Native.Controls;

/// <summary>
/// One line of text with its label above it. A hint helps fill it in; an error says what is wrong and what to do,
/// in words, never by color alone. The input is the platform's own, so the keyboard, the clipboard, selection, and
/// input methods behave as they do everywhere else on the device.
/// </summary>
public sealed class Field : StackPanel
{
    #region Private Fields

    private readonly TextBlock label = Styles.Text(null, "DvTextLabel");

    private readonly Facet frame = new();

    private readonly TextBlock placeholder = Styles.Text(null);

    private readonly TextBlock hint = Styles.Text(null, "DvTextCaption");

    private readonly TextBlock error = Styles.Text(null, "DvTextCaption");

    private Control input = new TextBox();

    private bool focused;

    #endregion

    #region Constructors

    public Field()
    {
        Spacing = Tokens.Number("DvSpace1");
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.TextWrapping = TextWrapping.NoWrap;
        placeholder.Margin = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0);
        AutomationProperties.SetAccessibilityView(placeholder, AccessibilityView.Raw);
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        frame.Cut = Tokens.Number("DvCutS");
        frame.MinHeight = Targets.Minimum;
        Children.Add(label);
        Children.Add(frame);
        Children.Add(hint);
        Children.Add(error);
        BuildInput();
        IsEnabledChangedBridge();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty LabelProperty = Dp.Register<Field, string>(nameof(Label), string.Empty, field => field.Describe());

    public static readonly DependencyProperty ValueProperty = Dp.Register<Field, string>(nameof(Value), string.Empty, field => field.ShowValue());

    public static readonly DependencyProperty TypeProperty = Dp.Register<Field, string>(nameof(Type), "text", field => field.BuildInput());

    public static readonly DependencyProperty PlaceholderProperty = Dp.Register<Field, string?>(nameof(Placeholder), null, field => field.Describe());

    public static readonly DependencyProperty HintProperty = Dp.Register<Field, string?>(nameof(Hint), null, field => field.Describe());

    public static readonly DependencyProperty ErrorProperty = Dp.Register<Field, string?>(nameof(Error), null, field => field.Describe());

    public static readonly DependencyProperty DisabledProperty = Dp.Register<Field, bool>(nameof(Disabled), false, field => field.Describe());

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The text in the field. It changes with every key, and can be bound both ways.</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>What the text is: "text", "email", "url", or "password", which hides it.</summary>
    public string Type
    {
        get => (string)GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>One line that helps fill the field in.</summary>
    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>What is wrong and what to do about it. Shown in words, never by color alone.</summary>
    public string? Error
    {
        get => (string?)GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    public bool Disabled
    {
        get => (bool)GetValue(DisabledProperty);
        set => SetValue(DisabledProperty, value);
    }

    /// <summary>Raised when Enter is pressed in the field, which sends the form the field is in.</summary>
    public event EventHandler? Submitted;

    #endregion

    #region Public Methods

    /// <summary>Moves the focus into the field, for when it takes the place of the control a person just used.</summary>
    public void TakeFocus() => input.Focus(FocusState.Keyboard);

    #endregion

    #region Private Methods

    // The platform has one control for text that is shown and another for text that is hidden.
    private void BuildInput()
    {
        frame.Children.Remove(input);
        frame.Children.Remove(placeholder);

        if (Type is "password")
        {
            var box = new PasswordBox { Template = (ControlTemplate)Styles.Dictionary["DvPasswordBoxTemplate"], Password = Value };
            box.PasswordChanged += (_, _) => Take(box.Password);
            input = box;
        }
#if ANDROID
        else if (Type is "url")
        {
            var box = new AndroidAddressEditor { Text = Value };
            box.TextChanged += (_, _) => Take(box.Text);
            box.Submitted += (_, _) => Submit();
            input = box;
        }
#endif
        else
        {
            var box = new TextBox
            {
                Template = Styles.Template("DvTextBoxTemplate"),
                Text = Value,
                InputScope = Scope(),
                IsSpellCheckEnabled = Type is not ("url" or "email"),
            };
            box.TextChanged += (_, _) => Take(box.Text);
            input = box;
        }

        input.FontFamily = Tokens.FontUi;
        input.FontSize = Tokens.Number("DvText14");
        input.Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0);
        input.VerticalAlignment = VerticalAlignment.Center;
        input.VerticalContentAlignment = VerticalAlignment.Center;
        input.MinHeight = Targets.Minimum;
        input.UseSystemFocusVisuals = false;
        input.GotFocus += (_, _) => SetFocused(true);
        input.LostFocus += (_, _) => SetFocused(false);
        input.KeyDown += (_, key) =>
        {
            if (key.Key is VirtualKey.Enter)
            {
#if ANDROID
                if (input is AndroidAddressEditor)
                {
                    key.Handled = true;
                }
#endif
                Submit();
            }
        };
        frame.Children.Add(input);
        frame.Children.Add(placeholder);
        Describe();
    }

    private void Submit()
    {
        // The change event for the last letters can still be on its way when Enter arrives, as it is in a browser.
        Take(input switch
        {
            PasswordBox box => box.Password,
#if ANDROID
            AndroidAddressEditor box => box.Text,
#endif
            _ => ((TextBox)input).Text,
        });
        Submitted?.Invoke(this, EventArgs.Empty);
    }

    private InputScope Scope() => new()
    {
        Names =
        {
            new InputScopeName(Type switch
            {
                "email" => InputScopeNameValue.EmailSmtpAddress,
                "url" => InputScopeNameValue.Url,
                _ => InputScopeNameValue.Default,
            }),
        },
    };

    private void Take(string text)
    {
        if (Value != text)
        {
            Value = text;
        }

        placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowValue()
    {
        switch (input)
        {
            case PasswordBox box when box.Password != Value:
                box.Password = Value;
                break;
            case TextBox box when box.Text != Value:
                box.Text = Value;
                break;
#if ANDROID
            case AndroidAddressEditor box when box.Text != Value:
                box.Text = Value;
                break;
#endif
        }

        placeholder.Visibility = Value.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetFocused(bool value)
    {
        focused = value;
        Paint();
    }

    private void IsEnabledChangedBridge() => RegisterPropertyChangedCallback(DisabledProperty, (_, _) => Paint());

    // The label names the input, and the hint and the error describe it, so all three are read with it.
    private void Describe()
    {
        label.Text = Label;
        placeholder.Text = Placeholder ?? string.Empty;
        placeholder.Visibility = Value.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        hint.Text = Hint ?? string.Empty;
        hint.Visibility = Hint is null ? Visibility.Collapsed : Visibility.Visible;
        error.Text = Error ?? string.Empty;
        error.Visibility = Error is null ? Visibility.Collapsed : Visibility.Visible;
        input.IsEnabled = !Disabled;
        AutomationProperties.SetName(input, Label);
        AutomationProperties.SetHelpText(input, string.Join(" ", new[] { Hint, Error }.Where(text => text is not null)));
        Paint();
    }

    private void Paint()
    {
        frame.Fill = Tokens.Brush(this, "DvBg0");
        frame.Edge = Tokens.Brush(this, focused ? "DvEmber" : Error is null ? "DvText3" : "DvDanger");
        frame.Ring = focused ? Tokens.Brush(this, "DvEmber") : null;
        var foreground = Disabled ? "DvText3" : "DvText";
        input.Foreground = Tokens.Brush(this, foreground);
#if ANDROID
        if (input is AndroidAddressEditor box)
        {
            box.Describe(Label, !Disabled, Tokens.Color(this, foreground));
        }
#endif
        placeholder.Foreground = Tokens.Brush(this, "DvText3");
        error.Foreground = Tokens.Brush(this, "DvDanger");
    }

    #endregion
}
