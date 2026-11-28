#if DEBUG
namespace Drover.Native.Pages;

/// <summary>
/// For taking a picture of the conversation's samples with no server: a development build started with
/// <c>DROVER_DEV_GALLERY</c> set to some words shows the gallery's conversation sections whose titles hold them, in
/// place of the first screen. <c>DROVER_DEV_THEME</c> names the theme to show them in. The word <c>pane</c> shows one
/// sample conversation over its composer and status line instead, as wide as <c>DROVER_DEV_WIDTH</c> says, light or
/// black when <c>DROVER_DEV_MODE</c> says so, and thinking or with a turn of two messages when
/// <c>DROVER_DEV_STATE</c> says <c>thinking</c> or <c>turn</c>. The word <c>splash</c> shows the real splash alone, light when that says so.
/// Not in a product build.
/// </summary>
internal static class DevGallery
{
    #region Public Methods

    public static void ShowIfAsked(Window window)
    {
        if (Environment.GetEnvironmentVariable("DROVER_DEV_GALLERY") is not { Length: > 0 } wanted)
        {
            return;
        }

        var black = Environment.GetEnvironmentVariable("DROVER_DEV_MODE") is "black";
        if (Environment.GetEnvironmentVariable("DROVER_DEV_THEME") is { } id && BuiltInThemes.Get(id) is { } theme)
        {
            ThemeTokens.Apply(theme, black);
        }
        else if (black)
        {
            ThemeTokens.Apply(ThemeTokens.Current, black);
        }

        if (wanted is "splash")
        {
            var light = Environment.GetEnvironmentVariable("DROVER_DEV_MODE") is "light";
            window.Content = new Splash { RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark };
            return;
        }

        if (wanted is "pane")
        {
            window.Content = Pane();
            return;
        }

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace5"), Padding = new(Tokens.Number("DvSpace4")) };
        foreach (var section in ChatSamples.Sections())
        {
            if (Title(section).Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                column.Children.Add(section);
            }
        }

        var page = new ScrollViewer
        {
            Content = column,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsTabStop = false,
        };
        page.WhenShownOrRethemed(() => page.Background = Tokens.Brush(page, "DvBg1"));
        window.Content = page;
    }

    #endregion

    #region Private Methods

    // The pieces a conversation is made of, put together as its pane puts them, on the conversation's own surface.
    private static Grid Pane()
    {
        var pane = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        pane.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        pane.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var wide = Environment.GetEnvironmentVariable("DROVER_DEV_WIDTH");
        if (double.TryParse(wide, System.Globalization.CultureInfo.InvariantCulture, out var width))
        {
            pane.Width = width;
        }

        var light = Environment.GetEnvironmentVariable("DROVER_DEV_MODE") is "light";
        pane.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        var state = Environment.GetEnvironmentVariable("DROVER_DEV_STATE");
        var working = state is "thinking";
        var list = state switch
        {
            "thinking" => ChatSamples.Thinking(),
            "turn" => ChatSamples.Turn(),
            _ => ChatSamples.Finished(),
        };
        var composer = ChatSamples.ComposerAndStatus(
            new() { BotName = "Sample lead bot", Running = working }, list.Status);
        Grid.SetRow(composer, 1);
        pane.Children.Add(new MessageList { Model = list });
        pane.Children.Add(composer);
        pane.WhenShownOrRethemed(() => pane.Background = Tokens.Brush(pane, "DvBg0"));
        return pane;
    }

    private static string Title(GallerySection section) =>
        section.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;

    #endregion
}
#endif
