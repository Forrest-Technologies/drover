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
#if ANDROID
        frame.Tapped += (_, _) =>
        {
            if (input is AndroidTextEditor editor)
            {
                editor.ShowKeyboard();
            }
        };
#endif
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

    public static readonly DependencyProperty MaxLengthProperty = Dp.Register<Field, int>(nameof(MaxLength), 0, field => field.Describe());

    public static readonly DependencyProperty AutofillProperty = Dp.Register<Field, string?>(nameof(Autofill), null, field => field.BuildInput());

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

    /// <summary>The most characters the field takes, or 0 for no limit. Typing or pasting past it does nothing.</summary>
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>
    /// What a password manager may fill in: "username", "current-password", or "new-password", the names a browser
    /// knows. Each platform is told in its own terms.
    /// </summary>
    public string? Autofill
    {
        get => (string?)GetValue(AutofillProperty);
        set => SetValue(AutofillProperty, value);
    }

    /// <summary>Raised when Enter is pressed in the field, which sends the form the field is in.</summary>
    public event EventHandler? Submitted;

    #endregion

    #region Public Methods

    /// <summary>Moves the focus into the field, for when it takes the place of the control a person just used.</summary>
    public void TakeFocus() => input.Focus(FocusState.Keyboard);

    #endregion

    #region Private Methods

    private void BuildInput()
    {
        frame.Children.Remove(input);
        frame.Children.Remove(placeholder);

        input = MakeInput();
        input.FontFamily = Tokens.FontUi;
        input.FontSize = Tokens.Number("DvText14");
        input.Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0);
        input.VerticalAlignment = VerticalAlignment.Center;
        input.VerticalContentAlignment = VerticalAlignment.Center;
        input.MinHeight = Targets.Minimum;
        input.UseSystemFocusVisuals = false;
        input.GotFocus += (_, _) =>
        {
            SetFocused(true);
            PlatformFields.Describe(Autofill);
        };
        input.LostFocus += (_, _) =>
        {
            SetFocused(false);
            PlatformFields.Describe(null);
        };
        input.KeyDown += (_, key) =>
        {
            if (key.Key is VirtualKey.Enter)
            {
#if ANDROID
                if (input is AndroidTextEditor)
                {
                    key.Handled = true;
                }
#endif
                Submit();
            }
        };
#if ANDROID
        if (input is AndroidTextEditor)
        {
            frame.Children.Add(placeholder);
            frame.Children.Add(input);
        }
        else
#endif
        {
            frame.Children.Add(input);
            frame.Children.Add(placeholder);
        }
        Describe();
    }

    // The platform has one control for text that is shown and another for text that is hidden.
    private Control MakeInput()
    {
#if ANDROID
        if (AndroidEditor() is { } editor)
        {
            editor.Text = Value;
            editor.TextChanged += (_, _) => Take(editor.Text);
            editor.Submitted += (_, _) => Submit();
            return editor;
        }

#endif
        if (Type is "password")
        {
            var box = new PasswordBox
            {
                Template = (ControlTemplate)Styles.Dictionary["DvPasswordBoxTemplate"],
                Password = Value,
                InputScope = Scope(InputScopeNameValue.Password),
            };
            box.PasswordChanged += (_, _) => Take(box.Password);
            return box;
        }

        var text = new TextBox
        {
            Template = Styles.Template("DvTextBoxTemplate"),
            Text = Value,
            InputScope = Scope(Type switch
            {
                "email" => InputScopeNameValue.EmailSmtpAddress,
                "url" => InputScopeNameValue.Url,
                _ => InputScopeNameValue.Default,
            }),
            IsSpellCheckEnabled = Type is not ("url" or "email"),
        };
        text.TextChanged += (_, _) => Take(text.Text);
        return text;
    }

    private void Submit()
    {
        // The change event for the last letters can still be on its way when Enter arrives, as it is in a browser.
        Take(input switch
        {
            PasswordBox box => box.Password,
#if ANDROID
            AndroidTextEditor box => box.Text,
#endif
            _ => ((TextBox)input).Text,
        });
        Submitted?.Invoke(this, EventArgs.Empty);
    }

    // What the text is, for the keyboard and for the system: the one hint a desktop platform takes.
    private static InputScope Scope(InputScopeNameValue name) => new() { Names = { new InputScopeName(name) } };

#if ANDROID
    // Android's autofill service reads hints from a real field only, so a field it may fill is one. An address is one
    // too, so input methods compose it as they do elsewhere.
    private AndroidTextEditor? AndroidEditor()
    {
        const Android.Text.InputTypes PlainText = Android.Text.InputTypes.ClassText;
        const Android.Text.InputTypes NoSuggestions = Android.Text.InputTypes.TextFlagNoSuggestions;
        return (Type, Autofill) switch
        {
            ("url", _) => new(PlainText | Android.Text.InputTypes.TextVariationUri | NoSuggestions),
            ("password", "current-password") => new(PlainText | Android.Text.InputTypes.TextVariationPassword, "password"),
            ("password", "new-password") => new(PlainText | Android.Text.InputTypes.TextVariationPassword, "newPassword"),
            (_, "username") => new(PlainText | Android.Text.InputTypes.TextVariationEmailAddress | NoSuggestions, "username"),
            _ => null,
        };
    }
#endif

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
            case AndroidTextEditor box when box.Text != Value:
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
        switch (input)
        {
            case TextBox box:
                box.MaxLength = MaxLength;
                break;
            case PasswordBox box:
                box.MaxLength = MaxLength;
                break;
        }

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
        if (input is AndroidTextEditor box)
        {
            box.Describe(Label, !Disabled, Tokens.Color(this, foreground), MaxLength);
        }
#endif
        placeholder.Foreground = Tokens.Brush(this, "DvText3");
        error.Foreground = Tokens.Brush(this, "DvDanger");
    }

    #endregion
}
