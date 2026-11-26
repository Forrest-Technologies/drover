namespace Drover.Engine.Tests.Extensions;

/// <summary>
/// Reading an extension file. The file is a stranger's, so anything the reader does not know refuses all of it: a
/// field, a kind of part, a theme that fails a theme check, a rule a bot could not have, a setting nothing uses.
/// Every value here is a sample.
/// </summary>
public sealed class ExtensionPackagesTests
{
    #region Private Fields

    private const string Server = """{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp"}""";

    private const string Rule = """{"kind":"rule","name":"Sample: ask before writing","checkpoint":"before_tool","effect":"ask","conditions":[{"kind":"tool_kind","toolKinds":["write"]}]}""";

    #endregion

    #region Reading

    [Fact]
    public void A_well_formed_file_reads_as_its_package_with_the_hash_of_its_text()
    {
        var text = File($"{Server},{Rule},{Theme()}");

        var reading = ExtensionPackages.Read(text);

        reading.Problem.Should().BeNull();
        reading.Package.Should().Match<ExtensionPackage>(package => package.Version == 2 && package.Id == "sample.one" && package.Name == "Sample");
        reading.Package!.Contents.Select(part => part.GetType()).Should().Equal([typeof(ConnectionRecipe), typeof(RuleTemplate), typeof(ThemePart)]);
        reading.Package.Themes().Should().ContainSingle().Which.Should().Match<PackagedTheme>(theme => theme.Id == "sample-dusk" && theme.Name == "Sample dusk");
        reading.Package.ToParts().Rules.Should().ContainSingle().Which.Should().Match<BotRuleBody>(
            rule => rule.Name == "Sample: ask before writing" && rule.Enabled && rule.Checkpoint == "before_tool" && rule.Effect == "ask" && rule.Position == null);
        reading.ContentHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void The_kind_of_a_part_may_stand_anywhere_among_its_fields()
    {
        var reading = ExtensionPackages.Read(File("""{"name":"Helpdesk","url":"https://tools.example.test/mcp","kind":"mcp_connection"}"""));

        reading.Problem.Should().BeNull();
        reading.Package!.Contents.Should().ContainSingle().Which.Should().BeOfType<ConnectionRecipe>();
    }

    #endregion

    #region Refusals

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_text_asks_for_a_file(string? text) =>
        ExtensionPackages.Read(text).Problem.Should().Be("Choose an extension file, or paste its text.");

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"version":2,"id":"sample.one"}""")]
    [InlineData("""{"version":"2","id":"sample.one","name":"Sample","contents":[]}""")]
    public void Text_that_is_not_an_extension_is_refused_in_a_sentence(string text)
    {
        var reading = ExtensionPackages.Read(text);

        reading.Package.Should().BeNull();
        reading.Problem.Should().StartWith("This is not an extension file Drover can read.");
    }

    [Theory]
    [InlineData("""{"version":2,"id":"sample.one","name":"Sample","grants":["everything"],"contents":[{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp"}]}""")]
    [InlineData("""{"version":2,"id":"sample.one","name":"Sample","contents":[{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","trust":"trusted"}]}""")]
    [InlineData("""{"version":2,"id":"sample.one","name":"Sample","contents":[{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","grants":["lookup"]}]}""")]
    [InlineData("""{"version":2,"id":"sample.one","name":"Sample","contents":[{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","clientSecret":"sample-value"}]}""")]
    [InlineData("""{"version":2,"id":"sample.one","name":"Sample","contents":[{"kind":"rule","name":"Sample","checkpoint":"before_tool","effect":"ask","botId":"0198a1c0-0000-7000-8000-000000000000"}]}""")]
    [InlineData("""{"version":2,"id":"sample.one","name":"Sample","contents":[{"kind":"rule","name":"Sample","checkpoint":"before_tool","effect":"ask","enabled":true}]}""")]
    [InlineData("""{"version":2,"id":"sample.one","name":"Sample","contents":[{"kind":"theme","theme":{},"default":true}]}""")]
    public void A_field_the_format_does_not_have_refuses_the_whole_file(string text)
    {
        var reading = ExtensionPackages.Read(text);

        reading.Package.Should().BeNull("a file has no field for a grant, for trust, for a credential, for a bot to apply a rule to, or for a default theme");
        reading.Problem.Should().StartWith("This is not an extension file Drover can read.");
    }

    [Fact]
    public void A_part_of_a_kind_this_server_does_not_know_refuses_the_whole_file_and_names_the_kind() =>
        ExtensionPackages.Read(File($$"""{{Server}},{"kind":"skill","name":"Sample skill"}"""))
            .Problem.Should().Be("This extension holds a part of the kind \"skill\", which this server does not know. Nothing was added.");

    [Fact]
    public void A_bot_in_a_file_is_refused_with_where_a_bot_goes() =>
        ExtensionPackages.Read("""{"version":1,"id":"sample.one","name":"Sample","contents":[{"kind":"bot","name":"Sample bot","instructions":""}]}""")
            .Problem.Should().Be("This extension holds a bot, and an extension no longer adds one. Nothing was added. A bot is shared as a bot file, from the bot's own menu, and imported on the Bots screen.");

    [Fact]
    public void A_part_with_no_kind_is_refused() =>
        ExtensionPackages.Read(File("""{"name":"Helpdesk","url":"https://tools.example.test/mcp"}"""))
            .Problem.Should().Be("This is not an extension file Drover can read. The trouble is at $.contents[0].");

    [Theory]
    [InlineData("""{"version":1,"id":"sample.one","name":"Sample","contents":[{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp"}]}""", "This extension is written in version 1 of the format. This server reads version 2.")]
    [InlineData("""{"version":2,"id":"Sample One","name":"Sample","contents":[{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp"}]}""", "The extension's id must be 3 to 100 lower-case letters, digits, dots, and hyphens, starting with a letter or digit.")]
    [InlineData("""{"version":2,"id":"sample.one","name":" ","contents":[{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp"}]}""", "The extension needs a name of up to 200 characters.")]
    [InlineData("""{"version":2,"id":"sample.one","name":"Sample","contents":[]}""", "This extension adds nothing: it has no contents.")]
    public void A_file_that_breaks_a_rule_of_the_format_is_refused_with_the_rule(string text, string problem) =>
        ExtensionPackages.Read(text).Problem.Should().Be(problem);

    [Fact]
    public void A_file_larger_than_an_extension_can_be_is_refused_before_it_is_read() =>
        ExtensionPackages.Read(new string('a', ExtensionPackages.MaxPackageBytes + 1)).Problem.Should().Be("This file is larger than an extension can be (256 KB).");

    [Fact]
    public void A_file_nested_deeper_than_an_extension_ever_is_is_refused()
    {
        var deep = $"{string.Concat(Enumerable.Repeat("{\"a\":", 40))}1{new string('}', 40)}";

        ExtensionPackages.Read(deep).Problem.Should().StartWith("This is not an extension file Drover can read.");
    }

    #endregion

    #region Tool Server

    [Fact]
    public void A_tool_server_reads_as_where_it_is_how_to_call_it_and_the_tools_it_suggests()
    {
        var reading = ExtensionPackages.Read(File("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","auth":"header","headerName":"X-Api-Key","suggestedTools":[{"name":"lookup","why":"Finds a ticket."},{"name":"close"}]}"""));

        reading.Problem.Should().BeNull();
        reading.Package!.Contents.Should().ContainSingle().Which.Should().BeEquivalentTo(new ConnectionRecipe("Helpdesk", "https://tools.example.test/mcp")
        {
            Auth = "header",
            HeaderName = "X-Api-Key",
            SuggestedTools = [new SuggestedTool("lookup", "Finds a ticket."), new SuggestedTool("close")],
        });
    }

    [Theory]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"ftp://tools.example.test/mcp"}""", "The tool server in this extension needs a name")]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://user:pass@tools.example.test/mcp"}""", "The tool server in this extension needs a name")]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp?key=sample"}""", "The tool server in this extension needs a name")]
    [InlineData("""{"kind":"mcp_connection","name":" ","url":"https://tools.example.test/mcp"}""", "The tool server in this extension needs a name")]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","auth":"basic"}""", "The tool server in this extension must say how Drover proves who is calling")]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","auth":"header"}""", "The tool server in this extension must name its header")]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","auth":"none","headerName":"X-Api-Key"}""", "The tool server in this extension must name its header")]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","auth":"header","headerName":"Host"}""", "The tool server in this extension must name its header")]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","headerTemplate":"{token}"}""", "The tool server in this extension gives a header value, and uses no header.")]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","suggestedTools":[{"name":""}]}""", "Each suggested tool needs the tool's name")]
    [InlineData("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","suggestedTools":[{"name":"lookup"},{"name":"lookup"}]}""", "An extension suggests each tool once.")]
    public void A_tool_server_that_breaks_a_rule_is_refused_with_the_rule(string part, string problem) =>
        ExtensionPackages.Read(File(part)).Problem.Should().StartWith(problem);

    [Fact]
    public void An_extension_names_one_tool_server_at_most() =>
        ExtensionPackages.Read(File($"{Server},{Server}")).Problem.Should().Be("An extension names one tool server at most. This one names more.");

    [Theory]
    [InlineData("https://tools.example.com/mcp", false)]
    [InlineData("https://8.8.8.8/mcp", false)]
    [InlineData("http://tools.example.com/mcp", true)]
    [InlineData("https://localhost/mcp", true)]
    [InlineData("https://127.0.0.1:9000/mcp", true)]
    [InlineData("https://[::1]/mcp", true)]
    [InlineData("https://192.168.1.20/mcp", true)]
    [InlineData("https://10.0.0.5/mcp", true)]
    [InlineData("https://169.254.169.254/latest", true)]
    [InlineData("https://nas/mcp", true)]
    [InlineData("https://tools.internal/mcp", true)]
    [InlineData("https://printer.local/mcp", true)]
    public void An_address_on_the_servers_own_network_or_not_https_is_local(string address, bool local) =>
        ExtensionPackages.IsLocal(new Uri(address)).Should().Be(local);

    #endregion

    #region Settings

    [Fact]
    public void Settings_read_with_their_types_and_fill_in_the_address_and_the_header()
    {
        var reading = ExtensionPackages.Read(Configured(
            """{"key":"region","label":"Region","type":"choice","choices":["eu","us"],"default":"eu"},{"key":"token","label":"API key","type":"secret","help":"Under Account, then API."}""",
            """{"kind":"mcp_connection","name":"Helpdesk","url":"https://{region}.tools.example.test/mcp","auth":"header","headerName":"Authorization","headerTemplate":"Bearer {token}"}"""));
        var recipe = reading.Package!.Contents.OfType<ConnectionRecipe>().Single();

        reading.Problem.Should().BeNull();
        reading.Package.Settings.Select(setting => (setting.Key, setting.Type, setting.Default)).Should().Equal([("region", "choice", "eu"), ("token", "secret", null)]);
        ExtensionPackages.SettingsIn(recipe.Url).Should().Equal(["region"]);
        ExtensionPackages.Fill(recipe.Url, new Dictionary<string, string> { ["region"] = "us" }).Should().Be("https://us.tools.example.test/mcp");
        ExtensionPackages.Fill(recipe.HeaderTemplate!, new Dictionary<string, string>()).Should().Be("Bearer {token}", "a setting with no value stays as the file writes it");
        recipe.ToPreview(new Dictionary<string, string>(), available: true).Should().Match<ExtensionConnectionPreview>(
            preview => preview.DependsOnSettings && preview.Host == "{region}.tools.example.test" && !preview.IsLocal && !preview.AsksForHeader);
        recipe.ToPreview(new Dictionary<string, string> { ["region"] = "eu" }, available: true).Should().Match<ExtensionConnectionPreview>(
            preview => !preview.DependsOnSettings && preview.Host == "eu.tools.example.test" && preview.Url == "https://eu.tools.example.test/mcp");
    }

    [Theory]
    [InlineData("""{"key":"Region","label":"Region","type":"text"}""", "https://{region}.tools.example.test/mcp", null, "A setting's key must be 1 to 40 lower-case letters, digits, and hyphens, starting with a letter.")]
    [InlineData("""{"key":"region","label":" ","type":"text"}""", "https://{region}.tools.example.test/mcp", null, "The setting \"region\" needs a label")]
    [InlineData("""{"key":"region","label":"Region","type":"password"}""", "https://{region}.tools.example.test/mcp", null, "The setting \"region\" must be of the type text, number, choice, toggle, or secret.")]
    [InlineData("""{"key":"region","label":"Region","type":"choice"}""", "https://{region}.tools.example.test/mcp", null, "The setting \"region\" must list its choices")]
    [InlineData("""{"key":"region","label":"Region","type":"text","choices":["eu"]}""", "https://{region}.tools.example.test/mcp", null, "The setting \"region\" must list its choices")]
    [InlineData("""{"key":"region","label":"Region","type":"choice","choices":["eu"],"default":"us"}""", "https://{region}.tools.example.test/mcp", null, "The setting \"region\" starts with a value it does not accept.")]
    [InlineData("""{"key":"port","label":"Port","type":"number","default":"many"}""", "https://tools.example.test:{port}/mcp", null, "The setting \"port\" starts with a value it does not accept.")]
    [InlineData("""{"key":"region","label":"Region","type":"text"},{"key":"region","label":"Again","type":"text"}""", "https://{region}.tools.example.test/mcp", null, "An extension names each setting once.")]
    [InlineData("""{"key":"region","label":"Region","type":"text"}""", "https://tools.example.test/mcp", null, "This extension declares the setting \"region\" and nothing in it uses it.")]
    [InlineData("""{"key":"region","label":"Region","type":"text"}""", "https://{region}.{zone}.example.test/mcp", null, "This extension uses a setting named \"zone\" that it does not declare.")]
    [InlineData("""{"key":"token","label":"API key","type":"secret"}""", "https://tools.example.test/{token}/mcp", null, "This extension puts the secret setting \"token\" in an address. A secret goes only in a header's value.")]
    [InlineData("""{"key":"token","label":"API key","type":"secret","default":"sample-value"}""", "https://tools.example.test/mcp", "Bearer {token}", "The secret setting \"token\" cannot have a value in the file.")]
    [InlineData("""{"key":"name","label":"Name","type":"text"}""", "https://tools.example.test/mcp", "Key {name}", "The header value in this extension must hold a secret setting")]
    public void A_setting_that_breaks_a_rule_is_refused_with_the_rule(string settings, string url, string? headerTemplate, string problem)
    {
        var header = headerTemplate is null ? string.Empty : $$""","auth":"header","headerName":"Authorization","headerTemplate":"{{headerTemplate}}" """;

        ExtensionPackages.Read(Configured(settings, $$"""{"kind":"mcp_connection","name":"Helpdesk","url":"{{url}}"{{header}}}""")).Problem.Should().StartWith(problem);
    }

    [Fact]
    public void A_header_value_written_in_a_file_is_refused_because_a_file_never_carries_the_credential() =>
        ExtensionPackages.Read(File("""{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp","auth":"header","headerName":"X-Api-Key","headerTemplate":"sample-value"}"""))
            .Problem.Should().StartWith("The header value in this extension must hold a secret setting");

    [Theory]
    [InlineData("text", "eu-west", null)]
    [InlineData("text", "", "Fill in Sample.")]
    [InlineData("text", "two\nlines", "Sample must be one line of up to 200 characters.")]
    [InlineData("number", "8443", null)]
    [InlineData("number", "many", "Sample must be a number.")]
    [InlineData("toggle", "true", null)]
    [InlineData("toggle", "yes", "Sample must be true or false.")]
    [InlineData("choice", "eu", null)]
    [InlineData("choice", "moon", "Sample must be one of its choices.")]
    [InlineData("secret", "two\nlines", null)]
    public void A_value_is_held_to_its_settings_type(string type, string value, string? problem) =>
        ExtensionPackages.FindValueProblem(new ExtensionSetting("sample", "Sample", type) { Choices = type is "choice" ? ["eu", "us"] : [] }, value).Should().Be(problem);

    #endregion

    #region Themes

    [Fact]
    public void A_theme_that_fails_a_theme_check_refuses_the_whole_file_and_says_which_check() =>
        ExtensionPackages.Read(File($$"""{"kind":"theme","theme":{{SampleThemes.Unreadable()}}}"""))
            .Problem.Should().StartWith("The theme \"sample-fog\" in this extension does not pass the theme checks:");

    [Fact]
    public void A_theme_may_not_take_one_of_drovers_own_ids() =>
        ExtensionPackages.Read(File(Theme("night-yard")))
            .Problem.Should().Be("The theme id \"night-yard\" is one of Drover's own. A theme in an extension needs another id.");

    [Fact]
    public void A_theme_is_held_once_and_must_be_a_theme_file()
    {
        ExtensionPackages.Read(File($"{Theme()},{Theme()}")).Problem.Should().Be("This extension holds the theme \"sample-dusk\" more than once.");
        ExtensionPackages.Read(File("""{"kind":"theme","theme":{"name":"Sample"}}""")).Problem.Should().Be("A theme in this extension is not a theme file: it has no id.");
        ExtensionPackages.Read(File("""{"kind":"theme","theme":{"id":"sample-dusk","name":"Sample","background":"url(https://example.test/a.png)"}}"""))
            .Problem.Should().StartWith("The theme \"sample-dusk\" in this extension does not pass the theme checks:", "a theme is the closed theme format, with no place for an address");
    }

    [Fact]
    public void An_extension_offers_a_bounded_number_of_themes()
    {
        var themes = string.Join(',', Enumerable.Range(0, ExtensionPackages.MaxThemes + 1).Select(_ => Theme()));

        ExtensionPackages.Read(File(themes)).Problem.Should().Be("An extension offers 8 themes at most.");
    }

    #endregion

    #region Rules

    [Theory]
    [InlineData("""{"kind":"rule","name":"Sample","checkpoint":"whenever","effect":"ask"}""", "A rule in this extension cannot be read. Choose when the rule is checked")]
    [InlineData("""{"kind":"rule","name":"Sample","checkpoint":"before_tool","effect":"allow"}""", "A rule in this extension cannot be read. Choose what the rule does")]
    [InlineData("""{"kind":"rule","name":" ","checkpoint":"before_tool","effect":"ask"}""", "A rule in this extension cannot be read.")]
    [InlineData("""{"kind":"rule","name":"Sample","checkpoint":"before_tool","effect":"ask","conditions":[{"kind":"made_up"}]}""", "A rule in this extension cannot be read.")]
    public void A_rule_a_bot_could_not_have_refuses_the_whole_file(string rule, string problem) =>
        ExtensionPackages.Read(File(rule)).Problem.Should().StartWith(problem);

    [Fact]
    public void An_extension_names_each_rule_once() =>
        ExtensionPackages.Read(File($"{Rule},{Rule}")).Problem.Should().Be("An extension names each rule once.");

    #endregion

    #region Kept Files

    [Fact]
    public void A_file_kept_from_the_first_version_reads_as_what_is_left_of_it_without_its_bot()
    {
        var kept = JsonElement.Parse("""{"version":1,"id":"sample.helpdesk","name":"Sample helpdesk","contents":[{"kind":"bot","name":"Sample scribe","instructions":"Keep notes short."},{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp"}]}""");
        var botOnly = JsonElement.Parse("""{"version":1,"id":"sample.note-taker","name":"Sample note taker","contents":[{"kind":"bot","name":"Sample scribe","instructions":"Keep notes short."}]}""");

        ExtensionPackages.ReadKept(kept)!.Contents.Should().ContainSingle().Which.Should().BeOfType<ConnectionRecipe>();
        ExtensionPackages.ReadKept(botOnly).Should().Match<ExtensionPackage>(
            package => package.Id == "sample.note-taker" && package.Contents.Count == 0,
            "its bot became an ordinary bot of the space when it was imported, so nothing of the extension is left but its row");
        ExtensionPackages.ReadKept(JsonElement.Parse("{}")).Should().BeNull("a kept file that cannot be read is shown as holding nothing, and can still be removed");
    }

    #endregion

    #region Helpers

    private static string File(string contents) => $$"""{"version":2,"id":"sample.one","name":"Sample","contents":[{{contents}}]}""";

    private static string Configured(string settings, string contents) =>
        $$"""{"version":2,"id":"sample.one","name":"Sample","settings":[{{settings}}],"contents":[{{contents}}]}""";

    private static string Theme(string id = "sample-dusk") => $$"""{"kind":"theme","theme":{{SampleThemes.File(id)}}}""";

    #endregion
}
