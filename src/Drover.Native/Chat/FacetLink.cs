namespace Drover.Native.Chat;

/// <summary>
/// A link drawn as a faceted row or chip: a bot in the crew, a side chat, a way to what opens beside the
/// conversation. At rest it may have no surface at all; the pointer gives it one, and the place the person is at
/// has a framed, lighter surface and brighter text, so it is never marked by color alone. Its colors are token names.
/// </summary>
internal sealed class FacetLink : Pressable
{
    #region Constructors

    public FacetLink()
        : base("DvFacetTemplate")
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        Click += (_, _) => Follow();
    }

    #endregion

    #region Properties

    /// <summary>Where the link goes: an address inside the app.</summary>
    public string? Address { get; set; }

    /// <summary>The surface at rest. None when null.</summary>
    public string? Fill { get; set; }

    public string? HoverFill { get; set; } = "DvBg2";

    public string? CurrentFill { get; set; } = "DvBg3";

    /// <summary>The one pixel line around the surface at rest. None when null.</summary>
    public string? Edge { get; set; }

    public string? HoverEdge { get; set; }

    public string? CurrentEdge { get; set; } = "DvText3";

    public string Ink { get; set; } = "DvText2";

    public string HoverInk { get; set; } = "DvText";

    /// <summary>True for the place the person is at: the bot being talked to, the open side chat, what is open beside the conversation.</summary>
    public bool IsCurrent { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>Marks the link as where the person is, in what is drawn and in what is read out.</summary>
    public void ShowCurrent(bool current, string status = "Current page")
    {
        if (current != IsCurrent)
        {
            IsCurrent = current;
            AutomationProperties.SetItemStatus(this, current ? status : string.Empty);
            Repaint();
        }
    }

    /// <summary>Paints again, after a color of the link was changed.</summary>
    public void Refresh() => Repaint();

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.Hyperlink);

    protected override void Paint()
    {
        var facet = (Facet)Frame!;
        var lit = IsEnabled && IsHovered;
        var fill = IsCurrent ? CurrentFill : lit ? HoverFill ?? Fill : Fill;
        var edge = IsCurrent ? CurrentEdge : lit ? HoverEdge ?? Edge : Edge;
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var ink = Tokens.Brush(this, IsCurrent || lit ? HoverInk : Ink);

        facet.Cut = Tokens.Number("DvCutS");
        facet.Fill = fill is null ? clear : Tokens.Brush(this, fill);
        facet.Edge = edge is null ? clear : Tokens.Brush(this, edge);
        facet.Ring = HasKeyboardFocus ? Tokens.Brush(this, "DvEmber") : null;
        Foreground = ink;
        InkParts(Content, ink);
    }

    // Words put in the link as elements take its color. A line of text that brings its own style keeps it.
    private static void InkParts(object? content, Brush ink)
    {
        switch (content)
        {
            case TextBlock { Style: null } words:
                words.Foreground = ink;
                break;
            case Icon icon:
                icon.Foreground = ink;
                break;
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    InkParts(child, ink);
                }

                break;
        }
    }

    private void Follow()
    {
        if (Address is { Length: > 0 } address)
        {
            Link.Opener?.Invoke(address);
        }
    }

    #endregion
}
