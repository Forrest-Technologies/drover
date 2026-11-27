namespace Drover.Native.Pages;

/// <summary>
/// What a person reads of an extension, before importing it and on its row afterwards: the tool server it names,
/// the settings it asks for, the themes it offers, and each rule in the words a bot's rule is read in.
/// </summary>
internal static class ExtensionReview
{
    #region Public Methods

    /// <summary>The second step of an import: everything the file holds, and what importing does and does not do.</summary>
    public static IEnumerable<UIElement> Of(ExtensionPreview preview)
    {
        if (!string.IsNullOrWhiteSpace(preview.Description))
        {
            yield return Parts.Small(preview.Description);
        }

        var id = new FlexPanel { Gap = Tokens.Number("DvSpace1") };
        id.Children.Add(Parts.Small("Extension id"));
        id.Children.Add(Parts.Machine(preview.PackageId, "DvText12", "DvText3"));
        yield return id;

        if (preview.AlreadyInstalled)
        {
            yield return new StatusNote { Kind = NoteKind.Info, Text = "This extension is already in this space, so it cannot be imported again." };
            yield break;
        }

        yield return Parts.Small("Importing gives no bot a tool or a rule and changes nobody's look. Each of those is a person's choice afterwards.");

        if (preview.Connection is { } connection)
        {
            yield return Ruled(Server(connection, preview.Parts.Settings));
        }

        foreach (var part in Offered(preview.Parts))
        {
            yield return Ruled(part);
        }
    }

    /// <summary>The themes and the rules an extension offers, each under its heading. Nothing when it offers neither.</summary>
    public static IEnumerable<StackPanel> Offered(ExtensionParts parts)
    {
        if (parts.Themes.Count > 0)
        {
            var themes = Block("Themes it offers");
            foreach (var theme in parts.Themes)
            {
                themes.Children.Add(Parts.Fact(theme.Name, Parts.Small(theme.Intent)));
            }

            themes.Children.Add(Parts.Small("Each person chooses a theme for themselves under Appearance, in the account menu."));
            yield return themes;
        }

        if (parts.Rules.Count > 0)
        {
            var rules = Block("Rules it offers");
            foreach (var rule in parts.Rules)
            {
                rules.Children.Add(Parts.Fact(rule.Name ?? string.Empty, Parts.Small(RuleWords.Sentence(rule.Checkpoint ?? string.Empty, rule.Effect ?? string.Empty, rule.Conditions ?? []))));
            }

            rules.Children.Add(Parts.Small("A rule is added to a bot in the bot's settings, under Rules. It applies to no bot until then."));
            yield return rules;
        }
    }

    /// <summary>A part of the review under a line, so each reads as its own block.</summary>
    public static Border Ruled(UIElement part)
    {
        var rule = new Border { Child = part, Padding = new(0, Tokens.Number("DvSpace3"), 0, 0), BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0) };
        rule.WhenShownOrRethemed(() => rule.BorderBrush = Tokens.Brush(rule, "DvLine"));
        return rule;
    }

    #endregion

    #region Private Methods

    private static StackPanel Block(string heading)
    {
        var block = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        block.Children.Add(Parts.Title(heading, AutomationHeadingLevel.Level3));
        return block;
    }

    private static StackPanel Server(ExtensionConnectionPreview connection, IReadOnlyList<ExtensionSetting> settings)
    {
        var server = Block("Names a tool server");
        server.Children.Add(Parts.Machine(connection.Host, "DvText20", "DvText"));

        var facts = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        facts.Children.Add(Parts.Fact("Called", Parts.Body(connection.Name)));
        facts.Children.Add(Parts.Fact("Full address", Parts.Machine(connection.Url, "DvText12", "DvText3")));
        facts.Children.Add(Parts.Fact("How Drover signs in to this server", Parts.Body(ExtensionWords.Auth(connection.Auth, connection.HeaderName, connection.AsksForHeader))));

        if (settings.Count > 0)
        {
            facts.Children.Add(Parts.Fact("Settings you fill in when you connect", Lines(settings.Select(setting => (setting.Label, setting.Help)))));
        }

        if (connection.SuggestedTools.Count > 0)
        {
            facts.Children.Add(Parts.Fact("Tools it suggests giving a bot", Lines(connection.SuggestedTools.Select(tool => (tool.Name, tool.Why)), machine: true)));
        }

        server.Children.Add(facts);

        if (!connection.Available)
        {
            server.Children.Add(new StatusNote
            {
                Kind = NoteKind.Info,
                Text = "Tool connections are turned off on this server, so the tool server is not connected. Everything else in the file is added.",
            });
            return server;
        }

        server.Children.Add(Parts.Small("Importing contacts nothing. You connect the server afterwards, and give its tools to a bot in the bot's settings."));

        if (connection.DependsOnSettings)
        {
            server.Children.Add(new StatusNote
            {
                Kind = NoteKind.Info,
                Text = "Where calls go depends on a setting. You see the full address, and are asked about a local one, when you connect.",
            });
        }
        else if (connection.IsLocal)
        {
            server.Children.Add(new StatusNote
            {
                Kind = NoteKind.Info,
                Text = "This address is on this server's own network, or is not https. You are asked again before it is connected.",
            });
        }

        return server;
    }

    /// <summary>A name and what is said of it, one pair a line.</summary>
    public static StackPanel Lines(IEnumerable<(string Name, string About)> pairs, bool machine = false)
    {
        var lines = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        foreach (var (name, about) in pairs)
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var said = Parts.Body(string.IsNullOrWhiteSpace(about) ? null : $": {about}");
            Grid.SetColumn(said, 1);
            line.Children.Add(machine ? Parts.Machine(name, "DvText12", "DvText3") : Parts.Body(name));
            line.Children.Add(said);
            lines.Children.Add(line);
        }

        return lines;
    }

    #endregion
}
