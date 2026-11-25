namespace Drover.Native.Pages;

/// <summary>The columns used inside a sheet and under a list card, spaced from tokens.</summary>
internal static class Forms
{
    #region Public Methods

    public static StackPanel Column(params UIElement[] children)
    {
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        foreach (var child in children)
        {
            column.Children.Add(child);
        }

        return column;
    }

    public static StackPanel SheetBody(params UIElement[] children)
    {
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace4") };
        foreach (var child in children)
        {
            column.Children.Add(child);
        }

        return column;
    }

    #endregion
}
