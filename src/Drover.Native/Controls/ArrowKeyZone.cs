namespace Drover.Native.Controls;

/// <summary>
/// A region whose items move the focus among themselves with the arrow keys, Home, and End. Each item marks itself
/// with <c>ArrowKeyZone.IsItem="True"</c>. The same key press never also scrolls the page behind, and only a press
/// on an item is taken: a field or a scrolling box inside the region keeps its keys. An item that moves the focus
/// its own way, as a timeline's nodes do, handles the key first and the region leaves it alone.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Items))]
public sealed class ArrowKeyZone : Grid
{
    #region Private Fields

    private readonly ContentPresenter host = new();

    #endregion

    #region Constructors

    public ArrowKeyZone()
    {
        Children.Add(host);
        KeyDown += (_, key) => Move(key);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty IsItemProperty =
        DependencyProperty.RegisterAttached("IsItem", typeof(bool), typeof(ArrowKeyZone), new PropertyMetadata(false));

    public static readonly DependencyProperty LabelProperty = Dp.Register<ArrowKeyZone, string>(
        nameof(Label), string.Empty, zone => AutomationProperties.SetName(zone, zone.Label));

    public static readonly DependencyProperty ItemsProperty =
        Dp.Register<ArrowKeyZone, object?>(nameof(Items), null, zone => zone.host.Content = zone.Items);

    /// <summary>What the region is, read to assistive technology as its name.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>What the region holds.</summary>
    public object? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    #endregion

    #region Public Methods

    public static bool GetIsItem(DependencyObject element) => (bool)element.GetValue(IsItemProperty);

    public static void SetIsItem(DependencyObject element, bool value) => element.SetValue(IsItemProperty, value);

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Group);

    private void Move(KeyRoutedEventArgs key)
    {
        if (key.Handled || key.OriginalSource is not Control from || !GetIsItem(from))
        {
            return;
        }

        List<Control> items = [.. ItemsUnder(this).Where(item => item.IsEnabled)];
        var current = items.IndexOf(from);
        var motion = key.Key switch
        {
            VirtualKey.Down or VirtualKey.Right => ArrowMotionKey.Next,
            VirtualKey.Up or VirtualKey.Left => ArrowMotionKey.Previous,
            VirtualKey.Home => ArrowMotionKey.First,
            VirtualKey.End => ArrowMotionKey.Last,
            _ => (ArrowMotionKey?)null,
        };

        if (motion is { } chosen && ArrowMotion.Next(current, items.Count, chosen) is { } index)
        {
            key.Handled = true;
            items[index].Focus(FocusState.Keyboard);
        }
    }

    private static IEnumerable<Control> ItemsUnder(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Control control && GetIsItem(control))
            {
                yield return control;
            }

            foreach (var deeper in ItemsUnder(child))
            {
                yield return deeper;
            }
        }
    }

    #endregion
}
