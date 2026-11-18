namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>DelegationCard</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class DelegationCard : Grid
{
    #region Constructors

    public DelegationCard()
    {
        Children.Add(Styles.Text(nameof(DelegationCard), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty ItemProperty = Dp.Register<DelegationCard, DelegationItem?>(nameof(Item), null);

    public static readonly DependencyProperty ChildContentProperty = Dp.Register<DelegationCard, UIElement?>(nameof(ChildContent), null, card => card.Show());

    public DelegationItem? Item
    {
        get => (DelegationItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public UIElement? ChildContent
    {
        get => (UIElement?)GetValue(ChildContentProperty);
        set => SetValue(ChildContentProperty, value);
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        while (Children.Count > 1)
        {
            Children.RemoveAt(1);
        }

        if (ChildContent is FrameworkElement content)
        {
            content.Margin = new(0, Tokens.Number("DvSpace5"), 0, 0);
            Children.Add(content);
        }
    }

    #endregion
}
