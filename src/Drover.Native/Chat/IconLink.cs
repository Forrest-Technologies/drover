namespace Drover.Native.Chat;

/// <summary>
/// A link that shows only an icon: the way back to the crew, the control that closes what is beside the
/// conversation. Where it goes is said in <see cref="Label"/>, which is its accessible name and its tooltip.
/// </summary>
internal sealed class IconLink : PlainButton
{
    #region Private Fields

    private readonly Icon icon = new() { Size = 20 };

    private string label = string.Empty;

    #endregion

    #region Constructors

    public IconLink()
    {
        Content = icon;
        Padding = new(0);
        (Ink, HoverInk) = ("DvText2", "DvText");
        MinWidth = Targets.Minimum;
        Click += (_, _) => Follow();
    }

    #endregion

    #region Properties

    /// <summary>Where the link goes: an address inside the app.</summary>
    public string? Address { get; set; }

    public IconName Icon
    {
        get => icon.NameOfIcon;
        set => icon.NameOfIcon = value;
    }

    /// <summary>Where the link goes, in words. It is the accessible name, so it is required.</summary>
    public string Label
    {
        get => label;
        set
        {
            label = value;
            AutomationProperties.SetName(this, value);
            ToolTipService.SetToolTip(this, value);
        }
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.Hyperlink);

    protected override void Painted() => icon.Foreground = CurrentInk;

    private void Follow()
    {
        if (Address is { Length: > 0 } address)
        {
            Link.Opener?.Invoke(address);
        }
    }

    #endregion
}
