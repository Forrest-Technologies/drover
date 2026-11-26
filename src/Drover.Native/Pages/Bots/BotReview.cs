namespace Drover.Native.Pages;

/// <summary>What a person reads of a bot file before importing it: the bot's name, its model, and every character it will be told.</summary>
internal static class BotReview
{
    #region Public Methods

    public static IEnumerable<UIElement> Of(BotImportPreview bot)
    {
        var facts = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        facts.Children.Add(Parts.Fact("Adds the bot", Parts.Body(bot.Name)));

        if (!string.IsNullOrWhiteSpace(bot.Description))
        {
            facts.Children.Add(Parts.Fact("What it is for", Parts.Body(bot.Description)));
        }

        if (bot.Model is not null)
        {
            facts.Children.Add(Parts.Fact("Thinks with", Parts.Body($"{bot.Model} on {bot.ModelConnection}. You can change this in the bot's settings.")));
        }

        facts.Children.Add(Parts.Fact("Tools", Parts.Body("Only what every new bot starts with. The file cannot give it more.")));
        facts.Children.Add(Parts.Fact("Starts", Parts.Body("Paused. It does nothing until you turn it on.")));
        yield return facts;
        yield return Instructions(bot);

        if (bot.Model is null)
        {
            var hint = new FlexPanel { Gap = 0 };
            hint.Children.Add(Parts.Small("A bot needs a model to think with, and this space has none it can use yet."));
            hint.Children.Add(Links.Plain("Connect a model", Addresses.Models));
            hint.Children.Add(Parts.Small(", then import this."));
            yield return hint;
        }
    }

    #endregion

    #region Private Methods

    private static StackPanel Instructions(BotImportPreview bot)
    {
        var shown = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        const string Heading = "What the bot will be told, in full";
        shown.Children.Add(Parts.Title(Heading, AutomationHeadingLevel.Level3));

        if (bot.HiddenCharacters > 0)
        {
            shown.Children.Add(new StatusNote { Kind = NoteKind.Danger, Text = BotFileWords.Hidden(bot.HiddenCharacters) });
        }

        if (string.IsNullOrEmpty(bot.Instructions))
        {
            shown.Children.Add(Parts.Small("The file gives this bot no instructions."));
            return shown;
        }

        // The instructions are shown whole: the frame grows with them and long lines wrap, so nothing is off the screen.
        var text = Parts.Machine(bot.Instructions, "DvText13", "DvText");
        text.IsTextSelectionEnabled = true;
        AutomationProperties.SetName(text, Heading);
        var box = new Border { Child = text, Padding = new(Tokens.Number("DvSpace3")), BorderThickness = new(Tokens.Number("DvBorder")) };
        box.WhenShownOrRethemed(() => (box.Background, box.BorderBrush) = (Tokens.Brush(box, "DvBg0"), Tokens.Brush(box, "DvLine")));
        shown.Children.Add(box);
        return shown;
    }

    #endregion
}
