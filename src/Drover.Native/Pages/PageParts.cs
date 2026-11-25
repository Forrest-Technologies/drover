namespace Drover.Native.Pages;

/// <summary>Token-based layout for the settings screens and their forms.</summary>
internal static class PageParts
{
    #region Public Methods

    /// <summary>The name at the head of a card: a small heading, a level below the screen's title.</summary>
    public static TextBlock CardTitle(string text)
    {
        var heading = Styles.Text(text, "DvTextH3");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        return heading;
    }

    /// <summary>A primary button's words after its icon, as the web client lays out a button with an icon.</summary>
    public static StackPanel WithIcon(IconName icon, string words)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        var mark = new Icon { NameOfIcon = icon, VerticalAlignment = VerticalAlignment.Center };
        mark.WhenShownOrRethemed(() => mark.Foreground = Tokens.Brush(mark, "DvOnAccent"));
        content.Children.Add(mark);
        content.Children.Add(new TextBlock { Text = words, VerticalAlignment = VerticalAlignment.Center });
        return content;
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

    /// <summary>The column inside a sheet, spaced from the sheet's own token.</summary>
    public static StackPanel SheetBody(params UIElement[] children)
    {
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace4") };
        foreach (var child in children)
        {
            column.Children.Add(child);
        }

        return column;
    }

    /// <summary>One action of an icon menu, with the name a person reads and the state it can be taken in.</summary>
    public static MenuItem MenuEntry(string label, Action choose, bool enabled = true)
    {
        var item = new MenuItem { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left, IsEnabled = enabled };
        item.Selected += (_, _) => choose();
        return item;
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
