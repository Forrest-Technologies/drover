namespace Drover.Native.Pages;

/// <summary>Token-based layout for the settings screens and their forms.</summary>
internal static class PageParts
{
    #region Public Methods

    public static TextBlock Title(string text)
    {
        var heading = Styles.Text(text, "DvTextH2");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        return heading;
    }

    public static StackPanel Column(params UIElement[] children)
    {
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        foreach (var child in children)
        {
            column.Children.Add(child);
        }

        return column;
    }

    public static FlowPanel Row(params UIElement[] children)
    {
        var row = new FlowPanel { Gap = Tokens.Number("DvSpace3") };
        foreach (var child in children)
        {
            row.Children.Add(child);
        }

        return row;
    }

    public static Facet Card(params UIElement[] children)
    {
        var card = new Facet { Padding = new(Tokens.Number("DvSpace4")) };
        card.Children.Add(Column(children));
        return card;
    }

    public static Button Action(string label, Func<Task> work, Action refresh, bool enabled = true, ButtonKind kind = ButtonKind.Quiet)
    {
        var button = new Button { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsEnabled = enabled, Kind = kind, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) =>
        {
            var pending = work();
            refresh();
            try
            {
                await pending;
            }
            finally
            {
                refresh();
            }
        };
        return button;
    }

    public static Field Field(string label, string value, Action<string> change, string type = "text", string? hint = null, bool disabled = false)
    {
        var field = new Field { Label = label, Value = value, Type = type, Hint = hint, Disabled = disabled };
        field.RegisterPropertyChangedCallback(Controls.Field.ValueProperty, (_, _) => change(field.Value));
        return field;
    }

    public static TextBlock Waiting(string words)
    {
        var note = Styles.Text(words, "DvTextSecondary");
        AutomationProperties.SetLiveSetting(note, AutomationLiveSetting.Polite);
        return note;
    }

    #endregion
}
