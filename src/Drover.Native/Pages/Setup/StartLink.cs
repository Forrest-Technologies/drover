namespace Drover.Native.Pages;

/// <summary>
/// The one action of a step that goes somewhere, so it is a link, with the primary look: the color of intent behind
/// words in the color that sits on it, across the width of the column.
/// </summary>
public sealed class StartLink : PlainButton
{
    #region Constructors

    public StartLink()
    {
        (Fill, HoverFill, Ink, HoverInk) = ("DvEmber", "DvEmber", "DvOnAccent", "DvOnAccent");
        Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0);
        FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Click += (_, _) => Follow();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty AddressProperty = Dp.Register<StartLink, string?>(nameof(Address), null);

    public static readonly DependencyProperty TextProperty =
        Dp.Register<StartLink, string>(nameof(Text), string.Empty, link => link.Content = new TextBlock { Text = link.Text, TextWrapping = TextWrapping.Wrap });

    /// <summary>Where the link goes: an address of <see cref="Addresses"/>.</summary>
    public string? Address
    {
        get => (string?)GetValue(AddressProperty);
        set => SetValue(AddressProperty, value);
    }

    /// <summary>The words of the link. They say where it goes.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.Hyperlink);

    private void Follow()
    {
        if (Address is { Length: > 0 } address)
        {
            Link.Opener?.Invoke(address);
        }
    }

    #endregion
}
