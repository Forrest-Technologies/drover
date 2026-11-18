namespace Drover.Native.Bots;

/// <summary>
/// A text field with the button that sends it: the field takes the row and the button sits at its foot beside it;
/// where the two do not fit, the button goes under the field.
/// </summary>
internal sealed class SandboxForm : Grid
{
    #region Private Fields

    private readonly Button send;

    private bool? stacked;

    #endregion

    #region Constructors

    public SandboxForm(Field field, Button send)
    {
        this.send = send;
        send.VerticalAlignment = VerticalAlignment.Bottom;
        send.HorizontalAlignment = HorizontalAlignment.Left;
        ColumnSpacing = RowSpacing = Tokens.Number("DvSpace2");
        Children.Add(field);
        Children.Add(send);
        SizeChanged += (_, _) => Fit();
        LayOut(under: false);
    }

    #endregion

    #region Private Methods

    private void Fit()
    {
        var needed = Tokens.Number("DvColumnMin") + Tokens.Number("DvSpace2") + send.ActualWidth;
        var under = ActualWidth > 0 && ActualWidth < needed;

        // Only a different outcome changes the layout, or changing it would ask for this again.
        if (under != stacked)
        {
            LayOut(under);
        }
    }

    private void LayOut(bool under)
    {
        stacked = under;
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });

        if (under)
        {
            RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        else
        {
            ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        }

        SetColumn(send, under ? 0 : 1);
        SetRow(send, under ? 1 : 0);
    }

    #endregion
}
