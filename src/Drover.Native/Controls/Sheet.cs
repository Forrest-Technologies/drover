namespace Drover.Native.Controls;

/// <summary>
/// A sheet is the dialog, placed at an edge: the trailing edge of a wide window, the bottom of a narrow one.
/// </summary>
public sealed class Sheet : Dialog
{
    public Sheet()
        : base(atEdge: true)
    {
    }
}
