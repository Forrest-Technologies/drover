namespace Drover.Native.Session;

/// <summary>
/// On a device only: which server the app is connected to, and the way to point it at another. In a browser the
/// app is served by its server, so there is no such choice and nothing is shown.
/// </summary>
public sealed class ServerChoice : StackPanel
{
    public ServerChoice()
    {
        if (((App)Application.Current).Services.GetService<IServerChoice>() is not { } choice)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Spacing = Tokens.Number("DvSpace3");
        Margin = new(0, Tokens.Number("DvSpace5"), 0, 0);

        var sentence = Styles.Text(null, "DvTextSecondary");
        sentence.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "This app is connected to " });
        sentence.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = choice.Host, FontFamily = Tokens.FontMono, FontSize = Tokens.Number("DvText12") });
        sentence.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "." });

        var change = new Button { Content = "Use another server", HorizontalAlignment = HorizontalAlignment.Left };
        change.Click += async (_, _) => await choice.Change();
        Children.Add(sentence);
        Children.Add(change);
    }
}
