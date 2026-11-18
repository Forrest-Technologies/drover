namespace Drover.Native.Pages;

/// <summary>
/// The small pieces the bot screens are built from in code: a hint, a line of machine text, and a way to show a
/// choice list or a control's visibility only when it changes, so a field being typed in keeps its focus.
/// </summary>
public static class BotParts
{
    #region Public Methods

    /// <summary>A hint under or beside a setting: secondary text, a size down (the web's dv-editor-hint).</summary>
    public static TextBlock Hint(string? text = null) => Small(text, "DvText2");

    /// <summary>Text a size down from the body, in the color of <paramref name="ink"/>.</summary>
    public static TextBlock Small(string? text, string ink)
    {
        var small = Styles.Text(text, "DvTextParagraph");
        small.FontSize = Tokens.Number("DvText13");
        small.LineHeight = (double)Styles.Dictionary["DvLine13"];
        return small.Inked(ink);
    }

    /// <summary>Text in the machine typeface: a handle, a tool's name, a count.</summary>
    public static TextBlock Mono(string? text = null) => Styles.Text(text, "DvTextMono");

    /// <summary>A label above a choice that names it for sighted people; the control carries the name for everyone else.</summary>
    public static TextBlock ChoiceLabel(string text)
    {
        var label = Styles.Text(text, "DvTextLabel");
        AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
        return label;
    }

    /// <summary>Colors the text with a token, in the theme it is shown in, now and whenever the theme changes.</summary>
    public static TextBlock Inked(this TextBlock text, string token)
    {
        text.WhenShownOrRethemed(() => text.Foreground = Tokens.Brush(text, token));
        return text;
    }

    public static T Shown<T>(this T element, bool shown)
        where T : UIElement
    {
        var visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (element.Visibility != visibility)
        {
            element.Visibility = visibility;
        }

        return element;
    }

    /// <summary>Gives a select its choices, only when they differ from the ones it has, and then its value.</summary>
    public static void Show(this Select select, IReadOnlyList<ChoiceOption> choices, string value)
    {
        if (!select.Options.Select(option => (option.Value, option.Label)).SequenceEqual(choices.Select(choice => (choice.Value, choice.Label))))
        {
            select.Options = [.. choices.Select(choice => new SelectOption(choice.Value, choice.Label))];
        }

        if (select.Value != value)
        {
            select.Value = value;
        }
    }

    public static void Show(this SegmentedControl control, IReadOnlyList<SegmentOption<string>> options, string value)
    {
        if (!control.Options.SequenceEqual(options))
        {
            control.Options = options;
        }

        if (control.Value != value)
        {
            control.Value = value;
        }
    }

    public static void Show(this Field field, string value)
    {
        if (field.Value != value)
        {
            field.Value = value;
        }
    }

    public static void Show(this TextArea area, string value)
    {
        if (area.Value != value)
        {
            area.Value = value;
        }
    }

    public static void Show(this Switch toggle, bool value)
    {
        if (toggle.Value != value)
        {
            toggle.Value = value;
        }
    }

    public static void Show(this TextBlock text, string? value)
    {
        if (text.Text != (value ?? string.Empty))
        {
            text.Text = value ?? string.Empty;
        }
    }

    public static void Show(this StatusNote note, string? value)
    {
        if (note.Text != value)
        {
            note.Text = value;
        }
    }

    /// <summary>Runs <paramref name="changed"/> with the field's text each time a person changes it.</summary>
    public static Field OnChanged(this Field field, Func<bool> quiet, Action<string> changed)
    {
        field.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) => Unless(quiet, () => changed(field.Value)));
        return field;
    }

    public static TextArea OnChanged(this TextArea area, Func<bool> quiet, Action<string> changed)
    {
        area.RegisterPropertyChangedCallback(TextArea.ValueProperty, (_, _) => Unless(quiet, () => changed(area.Value)));
        return area;
    }

    public static Select OnChanged(this Select select, Func<bool> quiet, Action<string> changed)
    {
        select.RegisterPropertyChangedCallback(Select.ValueProperty, (_, _) => Unless(quiet, () => changed(select.Value)));
        return select;
    }

    public static SegmentedControl OnChanged(this SegmentedControl control, Func<bool> quiet, Action<string> changed)
    {
        control.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) => Unless(quiet, () => changed(control.Value ?? string.Empty)));
        return control;
    }

    public static Switch OnChanged(this Switch toggle, Func<bool> quiet, Action<bool> changed)
    {
        toggle.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) => Unless(quiet, () => changed(toggle.Value)));
        return toggle;
    }

    /// <summary>A button that names its action and runs it.</summary>
    public static Button Action(string label, Action act, ButtonKind kind = ButtonKind.Quiet)
    {
        var button = new Button { Content = label, Kind = kind, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => act();
        return button;
    }

    /// <summary>A button whose words start with an icon, as "Write a rule" and "Add a routine" do.</summary>
    public static Button IconAction(IconName icon, string label, Action act)
    {
        var mark = new Icon { NameOfIcon = icon, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        content.Children.Add(mark);
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, label);
        button.RegisterPropertyChangedCallback(Control.ForegroundProperty, (_, _) => mark.Foreground = button.Foreground);
        button.Click += (_, _) => act();
        return button;
    }

    /// <summary>A column of parts with a gap from the spacing scale between them, or none for null.</summary>
    public static StackPanel Column(string? gap, params UIElement[] children)
    {
        var column = new StackPanel { Spacing = gap is null ? 0 : Tokens.Number(gap) };
        foreach (var child in children)
        {
            column.Children.Add(child);
        }

        return column;
    }

    /// <summary>Puts children in a panel in place of the ones it has.</summary>
    public static void Fill(this Panel panel, IEnumerable<UIElement> children)
    {
        panel.Children.Clear();
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }
    }

    #endregion

    #region Private Methods

    private static void Unless(Func<bool> quiet, Action act)
    {
        if (!quiet())
        {
            act();
        }
    }

    #endregion
}
