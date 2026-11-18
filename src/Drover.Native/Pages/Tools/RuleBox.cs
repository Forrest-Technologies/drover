namespace Drover.Native.Pages;

/// <summary>
/// One row of a list, set off from the one above it by a line, with room above and below its words: a row of the
/// status report, a run of a bot, a tool of a server. The room is a spacing token, named.
/// </summary>
public sealed class RuleBox : Border
{
    #region Constructors

    public RuleBox()
    {
        Paint();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty SpaceProperty = Dp.Register<RuleBox, string>(nameof(Space), "DvSpace3", box => box.Paint());

    /// <summary>The room above and below the words: the name of a spacing token. None when empty.</summary>
    public string Space
    {
        get => (string)GetValue(SpaceProperty);
        set => SetValue(SpaceProperty, value);
    }

    #endregion

    #region Private Methods

    private void Paint()
    {
        var room = Space.Length == 0 ? 0 : Tokens.Number(Space);
        BorderBrush = Tokens.Brush(this, "DvLine");
        BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        Padding = new(0, room, 0, room);
    }

    #endregion
}
