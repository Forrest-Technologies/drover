namespace Drover.Native.Controls;

/// <summary>
/// Where toasts show: the lower trailing corner, over the screen and never blocking it. The quiet ones are read
/// out when there is a pause; an error interrupts. A toast a person is reading or reaching for stays.
/// </summary>
public sealed class ToastHost : Grid
{
    #region Private Fields

    private readonly StackPanel quiet = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly StackPanel errors = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly HashSet<long> shown = [];

    private ToastHostViewModel? model;

    #endregion

    #region Constructors

    public ToastHost()
    {
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new(Tokens.Number("DvSpace4"));
        MaxWidth = Tokens.Number("DvToastWidth");
        AutomationProperties.SetName(this, "Notifications");
        AutomationProperties.SetLiveSetting(quiet, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(errors, AutomationLiveSetting.Assertive);

        var lists = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        lists.Children.Add(quiet);
        lists.Children.Add(errors);
        Children.Add(lists);
        ActualThemeChanged += (_, _) => Show();
    }

    #endregion

    #region Properties

    /// <summary>The toasts to show. The shell sets it once.</summary>
    public ToastHostViewModel? Model
    {
        get => model;
        set
        {
            model?.Changed -= Refresh;
            model = value;
            model?.Changed += Refresh;

            Show();
        }
    }

    #endregion

    #region Private Methods

    // The toast service speaks from whatever thread its work ended on.
    private void Refresh() => DispatcherQueue.TryEnqueue(Show);

    private void Show()
    {
        Fill(quiet, model?.Quiet ?? []);
        Fill(errors, model?.Errors ?? []);
        shown.IntersectWith((model?.Quiet ?? []).Concat(model?.Errors ?? []).Select(toast => toast.Id));
    }

    private void Fill(StackPanel list, IReadOnlyList<ToastMessage> toasts)
    {
        list.Children.Clear();
        foreach (var toast in toasts)
        {
            var card = Card(toast);
            list.Children.Add(card);

            if (shown.Add(toast.Id))
            {
                Motion.FadeIn(card);
                FrameworkElementAutomationPeer.FromElement(list)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }
    }

    private Border Card(ToastMessage toast)
    {
        var message = Styles.Text(null);
        if (ToastHostViewModel.KindWord(toast.Kind) is { } kind)
        {
            message.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = $"{kind} ",
                FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightHeading"],
                Foreground = Tokens.Brush(this, toast.Kind is ToastKind.Error ? "DvDanger" : "DvWarn"),
            });
        }

        message.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = toast.Message });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Number("DvSpace2"),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (toast.Action is { } action)
        {
            var act = new Button { Content = action.Label };
            if (action.Name is not null)
            {
                AutomationProperties.SetName(act, action.Name);
            }

            act.Click += async (_, _) => await model!.Act(toast);
            actions.Children.Add(act);
        }

        var dismiss = new Button { Content = "Dismiss" };
        dismiss.Click += (_, _) => model?.Dismiss(toast);
        actions.Children.Add(dismiss);

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        column.Children.Add(message);
        column.Children.Add(actions);

        var card = new Border
        {
            Child = column,
            Padding = new(Tokens.Number("DvSpace3")),
            BorderThickness = new(Tokens.Number("DvBorder")),
            CornerRadius = new(Tokens.Number("DvRadiusCard")),
            Background = Tokens.Brush(this, "DvBg2"),
            BorderBrush = Tokens.Brush(this, "DvLine"),
        };
        card.GotFocus += (_, _) => model?.Hold(toast);
        return card;
    }

    #endregion
}
