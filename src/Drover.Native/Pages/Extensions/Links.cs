namespace Drover.Native.Pages;

/// <summary>The links of the extensions screens: the main text color, underlined, as tall as a finger.</summary>
internal static class Links
{
    #region Public Methods

    public static Link Plain(string text, string address) => new()
    {
        Text = text,
        Address = address,
        Ink = "DvText",
        HoverInk = "DvText",
        MinHeight = Targets.Minimum,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    #endregion
}
