namespace Drover.Native.Chat;

/// <summary>
/// The line under a message that has versions, or may have a new one: the switch between the versions, which says
/// "2 of 3" between the previous and the next, and the one way to make a new version, Edit under a person's last
/// message and Write another reply under the last reply. Each control works by keyboard and is named.
/// </summary>
public sealed class MessageVersions : StackPanel
{
    #region Private Fields

    private readonly StackPanel switcher = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace1") };

    private readonly IconButton previous = new() { Icon = IconName.ChevronLeft, Label = ChatRows.PreviousVersion };

    private readonly IconButton next = new() { Icon = IconName.ChevronRight, Label = ChatRows.NextVersion };

    private readonly TextBlock place = Styles.Text(null, "DvTextCaption");

    private readonly PlainButton action = new()
    {
        Ink = "DvText2",
        HoverInk = "DvText",
        MinHeight = Targets.Minimum,
        Padding = Tokens.Space("DvSpace2", "DvSpace1"),
        FontSize = Tokens.Number("DvText13"),
    };

    private readonly TextBlock actionWords = new() { VerticalAlignment = VerticalAlignment.Center };

    private VersionSwitch? shown;

    #endregion

    #region Constructors

    public MessageVersions()
    {
        Orientation = Orientation.Horizontal;
        Spacing = Tokens.Number("DvSpace2");
        place.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(place, AccessibilityView.Raw);
        switcher.Children.Add(previous);
        switcher.Children.Add(place);
        switcher.Children.Add(next);
        AutomationProperties.SetLandmarkType(switcher, AutomationLandmarkType.Custom);

        action.Content = actionWords;

        previous.Click += async (_, _) => await Switch(shown?.Previous);
        next.Click += async (_, _) => await Switch(shown?.Next);
        action.Click += async (_, _) => await Act();
        Children.Add(switcher);
        Children.Add(action);
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    /// <summary>Shows the version named, in place of the one shown.</summary>
    public Func<Guid, Task>? ShowVersion { get; set; }

    /// <summary>Makes a new version: opens the edit of a person's message, or asks for another reply.</summary>
    public Func<Task>? OnAct { get; set; }

    /// <summary>True when there is anything on the line to use.</summary>
    public bool HasAny => switcher.Visibility is Visibility.Visible || action.Visibility is Visibility.Visible;

    #endregion

    #region Public Methods

    /// <summary>Shows where the message stands among its versions and the way to a new one, as far as it has them.</summary>
    public void Show(ChatMessage message)
    {
        shown = ShowVersion is null ? null : message.Versions;
        switcher.Visibility = shown is null ? Visibility.Collapsed : Visibility.Visible;

        if (shown is { } versions)
        {
            place.Text = ChatRows.VersionLabel(versions);
            AutomationProperties.SetName(switcher, ChatRows.VersionName(versions, message.FromPerson));
            previous.IsEnabled = versions is { Enabled: true, Previous: not null };
            next.IsEnabled = versions is { Enabled: true, Next: not null };
        }

        var offered = OnAct is not null && (message.FromPerson ? message.MayEdit : message.MayRegenerate);
        action.Visibility = offered ? Visibility.Visible : Visibility.Collapsed;
        actionWords.Text = message.FromPerson ? ChatRows.EditLabel : ChatRows.RegenerateLabel;
        AutomationProperties.SetName(action, actionWords.Text);
        Paint();
    }

    /// <summary>Puts the focus back on the way to a new version, for when the edit it opened is closed.</summary>
    public void TakeFocus() => action.TakeFocus();

    #endregion

    #region Private Methods

    private async Task Switch(Guid? version)
    {
        if (version is { } id && ShowVersion is { } show)
        {
            await show(id);
        }
    }

    private async Task Act()
    {
        if (OnAct is { } act)
        {
            await act();
        }
    }

    private void Paint()
    {
        place.Foreground = Tokens.Brush(this, "DvText2");
    }

    #endregion
}
