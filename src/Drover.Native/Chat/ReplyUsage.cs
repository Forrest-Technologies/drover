namespace Drover.Native.Chat;

/// <summary>
/// What a finished reply used, behind one small control under its words. Read only when asked for: a conversation
/// with many replies makes no request until a person opens one.
/// </summary>
public sealed class ReplyUsage : StackPanel
{
    #region Private Fields

    private readonly PlainButton toggle = new()
    {
        Ink = "DvText3",
        HoverInk = "DvText2",
        HoverFill = null,
        Radius = null,
        Padding = new(0),
        HorizontalAlignment = HorizontalAlignment.Left,
        FontSize = Tokens.Number("DvText12"),
    };

    private readonly Icon mark = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace1"), HorizontalAlignment = HorizontalAlignment.Left };

    private ReplyUsageViewModel? model;

    #endregion

    #region Constructors

    public ReplyUsage()
    {
        Spacing = Tokens.Number("DvSpace1");
        Margin = new(0, Tokens.Number("DvSpace2"), 0, 0);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace1") };
        content.Children.Add(new TextBlock { Text = ReplyUsageViewModel.Label, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(mark);
        toggle.Content = content;
        toggle.Click += async (_, _) => await Toggle();
        Children.Add(toggle);
        Children.Add(body);
    }

    #endregion

    #region Properties

    /// <summary>The reply's line: which run, and how to read what it used.</summary>
    public ReplyUsageViewModel? Model
    {
        get => model;
        set
        {
            model?.PropertyChanged -= Changed;
            model = value;
            model?.PropertyChanged += Changed;
            Show();
        }
    }

    #endregion

    #region Private Methods

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change) => Show();

    private async Task Toggle()
    {
        if (model is not null)
        {
            await model.Toggle();
        }
    }

    private void Show()
    {
        var open = model?.Open ?? false;
        mark.NameOfIcon = open ? IconName.ChevronDown : IconName.ChevronRight;
        mark.Foreground = toggle.CurrentInk;
        AutomationProperties.SetItemStatus(toggle, open ? "Expanded" : "Collapsed");
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        body.Children.Clear();

        if (model is null || !open)
        {
            return;
        }

        if (model.Problem is { } problem)
        {
            body.Children.Add(Line(problem, AutomationLiveSetting.Assertive));
            var again = new Button { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Left, Command = model.ReadCommand };
            body.Children.Add(again);
        }
        else if (model.IsAdding)
        {
            body.Children.Add(Line(ReplyUsageViewModel.Adding, AutomationLiveSetting.Polite));
        }
        else
        {
            foreach (var line in model.Lines)
            {
                body.Children.Add(Line(line, AutomationLiveSetting.Off));
            }
        }
    }

    private TextBlock Line(string text, AutomationLiveSetting live)
    {
        var line = Styles.Text(text, "DvTextCaption");
        line.Foreground = Tokens.Brush(this, "DvText2");
        AutomationProperties.SetLiveSetting(line, live);
        return line;
    }

    #endregion
}
