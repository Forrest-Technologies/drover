namespace Drover.Native.Controls;

/// <summary>
/// The one header at the top of a work screen: where the person is, the way back, and the screen's actions. It has no
/// surface of its own and sits directly on the yard. Its title is the page's level one heading, and only its actions
/// and its back button can take the focus.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Actions))]
public sealed class ScreenHeader : Grid
{
    #region Private Fields

    private readonly StackPanel actions = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = Tokens.Number("DvSpace1"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock title = Styles.Text(string.Empty, "DvTextH2");

    private IconButton? back;

    #endregion

    #region Constructors

    public ScreenHeader()
    {
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        ColumnSpacing = Tokens.Number("DvSpace2");
        MinHeight = Targets.Minimum;

        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        title.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        SetColumn(title, 1);
        SetColumn(actions, 2);
        Children.Add(title);
        Children.Add(actions);
        ApplyTitle();
        ApplyBack();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TitleProperty =
        Dp.Register<ScreenHeader, string>(nameof(Title), string.Empty, header => header.ApplyTitle());

    public static readonly DependencyProperty BackLabelProperty =
        Dp.Register<ScreenHeader, string?>(nameof(BackLabel), null, header => header.ApplyBack());

    public static readonly DependencyProperty BackAddressProperty =
        Dp.Register<ScreenHeader, string?>(nameof(BackAddress), null, header => header.ApplyBack());

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Where the back button goes, in words. It is the button's accessible name and tooltip.</summary>
    public string? BackLabel
    {
        get => (string?)GetValue(BackLabelProperty);
        set => SetValue(BackLabelProperty, value);
    }

    public string? BackAddress
    {
        get => (string?)GetValue(BackAddressProperty);
        set => SetValue(BackAddressProperty, value);
    }

    /// <summary>Trailing controls: an icon button, or a menu with an icon trigger and no words on it.</summary>
    public UIElementCollection Actions => actions.Children;

    #endregion

    #region Private Methods

    private void ApplyTitle() => title.Text = Title;

    private void ApplyBack()
    {
        if (BackLabel is { Length: > 0 } label && BackAddress is { Length: > 0 })
        {
            if (back is null)
            {
                back = new IconButton { Icon = IconName.ArrowLeft, Label = label, VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetAutomationId(back, "ScreenBack");
                back.Click += (_, _) => GoBack();
                SetColumn(back, 0);
                Children.Insert(0, back);
            }
            else
            {
                back.Label = label;
            }

            return;
        }

        if (back is not null)
        {
            Children.Remove(back);
            back = null;
        }
    }

    private void GoBack()
    {
        if (BackAddress is { Length: > 0 } address)
        {
            ((App)Application.Current).Services.GetRequiredService<Navigator>().NavigateTo(address);
        }
    }

    #endregion
}
