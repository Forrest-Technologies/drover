namespace Drover.Native.Controls;

/// <summary>What a frame is, which sets how big its corner is: a control, or a larger frame such as a card.</summary>
public enum FacetRole
{
    /// <summary>
    /// A larger frame: a card, a message bubble, the composer. Its corner is the theme's frame cut, or its card
    /// radius.
    /// </summary>
    Frame,

    /// <summary>A control: a button, a field. Its corner is the theme's control cut, or its control radius.</summary>
    Control,
}
