namespace Drover.Native.Controls;

/// <summary>
/// A link: words in the color of intent that take a person to an address. An address inside the app goes through
/// the app's navigator, so it is one step of history; any other address opens outside the app.
/// </summary>
public sealed class Link : PlainButton
{
    #region Constructors

    public Link()
    {
        (Ink, HoverInk, HoverFill, Radius) = ("DvEmber", "DvEmber", null, null);
        MinHeight = 0;
        Padding = new(0);
        HorizontalAlignment = HorizontalAlignment.Left;
        FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        Click += (_, _) => Follow();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty AddressProperty = Dp.Register<Link, string?>(nameof(Address), null);

    public static readonly DependencyProperty TextProperty =
        Dp.Register<Link, string>(nameof(Text), string.Empty, link => link.Content = new TextBlock
        {
            Text = link.Text,
            TextDecorations = Windows.UI.Text.TextDecorations.Underline,
            TextWrapping = TextWrapping.Wrap,
        });

    /// <summary>Where the link goes: an address of <see cref="Addresses"/>, or an absolute address outside the app.</summary>
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

    /// <summary>Takes an address inside the app to its screen. The app sets it once, at startup.</summary>
    public static Action<string>? Opener { get; set; }

    #endregion

    #region Public Methods

    internal static async Task OpenOutside(Uri address)
    {
        var opened = await Launcher.LaunchUriAsync(address);
#if __WASM__
        if (!opened && OperatingSystem.IsBrowser())
        {
            Browser.OpenOutside(address.AbsoluteUri);
        }
#endif
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.Hyperlink);

    private void Follow()
    {
        if (Address is not { Length: > 0 } address)
        {
            return;
        }

        if (Uri.TryCreate(address, UriKind.Absolute, out var outside) && outside.Scheme is "http" or "https" or "mailto")
        {
            _ = OpenOutside(outside);
            return;
        }

        Opener?.Invoke(address);
    }

    #endregion
}
