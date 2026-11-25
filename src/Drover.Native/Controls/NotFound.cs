namespace Drover.Native.Controls;

/// <summary>What is shown at an address that has no screen.</summary>
public sealed class NotFound : StackPanel
{
    public NotFound()
    {
        Spacing = Tokens.Number("DvSpace3");
        Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace5"), Tokens.Number("DvSpace4"), Tokens.Number("DvSpace5"));
        Children.Add(Styles.Text("Not found", "DvTextH1"));
        Children.Add(Styles.Text("There is no page at this address."));
        Children.Add(new Link { Text = "Back to conversations", Address = Addresses.Home });
    }
}
