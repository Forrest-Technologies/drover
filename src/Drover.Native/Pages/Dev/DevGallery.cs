#if DEBUG
namespace Drover.Native.Pages;

/// <summary>
/// For taking a picture of the conversation's samples with no server: a development build started with
/// <c>DROVER_DEV_GALLERY</c> set to some words shows the gallery's conversation sections whose titles hold them, in
/// place of the first screen. <c>DROVER_DEV_THEME</c> names the theme to show them in. The word <c>pane</c> shows one
/// sample conversation over its composer and status line instead, as wide as <c>DROVER_DEV_WIDTH</c> says and light
/// when <c>DROVER_DEV_MODE</c> says so. Not in a product build.
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

        if (Environment.GetEnvironmentVariable("DROVER_DEV_THEME") is { } id && BuiltInThemes.Get(id) is { } theme)
        {
            ThemeTokens.Apply(theme);
        }

        if (wanted is "pane")
        {
            window.Content = Pane();
            return;
        }

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace5"), Padding = new(Tokens.Number("DvSpace4")) };
        foreach (var section in ChatSamples.Sections().Where(section => Title(section).Contains(wanted, StringComparison.OrdinalIgnoreCase)))
        {
            column.Children.Add(section);
        }

        var page = new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsTabStop = false };
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
        if (double.TryParse(Environment.GetEnvironmentVariable("DROVER_DEV_WIDTH"), System.Globalization.CultureInfo.InvariantCulture, out var width))
        {
            pane.Width = width;
        }

        pane.RequestedTheme = Environment.GetEnvironmentVariable("DROVER_DEV_MODE") is "light" ? ElementTheme.Light : ElementTheme.Dark;
        var working = Environment.GetEnvironmentVariable("DROVER_DEV_STATE") is "thinking";
        var composer = new Composer
        {
            Model = new() { BotName = "Sample lead bot", Running = working },
            Under = new ConversationStatus { Model = ChatSamples.Status(working, finished: true) },
        };
        Grid.SetRow(composer, 1);
        pane.Children.Add(new MessageList { Model = working ? ChatSamples.Thinking() : ChatSamples.Finished() });
        pane.Children.Add(composer);
        pane.WhenShownOrRethemed(() => pane.Background = Tokens.Brush(pane, "DvBg0"));
        return pane;
    }

    private static string Title(GallerySection section) => section.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;

    #endregion
}
#endif
