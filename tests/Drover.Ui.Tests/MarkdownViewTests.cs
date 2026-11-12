namespace Drover.Ui.Tests;

public sealed class MarkdownViewTests : BunitContext
{
    #region Private Fields

    private const string ForbiddenElements =
        "script, iframe, style, svg, math, object, embed, form, link, meta, base, details, [style], [srcdoc]";

    private static readonly string[] allowedAddressStarts = ["http://", "https://", "mailto:"];

    private static readonly string[] allowedImageStarts = ["http://", "https://"];

    #endregion

    #region Properties

    public static TheoryData<string> HostileTexts =>
    [
        "<script>alert(1)</script>",
        "before <script>alert(1)</script> after",
        "<script src=\"https://example.org/x.js\"></script>",
        "<img src=x onerror=alert(1)>",
        "<img src=\"https://example.org/a.png\" onerror=\"alert(1)\">",
        "<a href=\"javascript:alert(1)\" onclick=\"alert(1)\">x</a>",
        "<a href=\"jav&#x0A;ascript:alert(1)\">x</a>",
        "<div onclick=\"alert(1)\">\n\nhello\n\n</div>",
        "<p style=\"position:fixed\" onmouseover=\"alert(1)\">hello</p>",
        "<iframe src=\"https://example.org\"></iframe>",
        "<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>",
        "<style>body { display: none }</style>",
        "<svg onload=alert(1)>",
        "<svg><script>alert(1)</script></svg>",
        "<math><mi xlink:href=\"javascript:alert(1)\">x</mi></math>",
        "<form action=\"javascript:alert(1)\"><button formaction=\"javascript:alert(1)\">x</button></form>",
        "<object data=\"javascript:alert(1)\"></object><embed src=\"javascript:alert(1)\">",
        "<base href=\"javascript:alert(1)//\"><meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<link rel=\"stylesheet\" href=\"https://example.org/x.css\">",
        "[x](javascript:alert(1))",
        "[x](JaVaScRiPt:alert(1))",
        "[x](  javascript:alert(1))",
        "[x](<javascript:alert(1)>)",
        "[x](<java script:alert(1)>)",
        "[x](java\tscript:alert(1))",
        "[x](java&#x09;script:alert(1))",
        "[x](java&#10;script:alert(1))",
        "[x](&#106;avascript:alert(1))",
        "[x](&#x6A;avascript&#58;alert(1))",
        "[x](javascript&colon;alert(1))",
        "[x](&#1;javascript:alert(1))",
        "[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)",
        "[x](data:text/html,<script>alert(1)</script>)",
        "[x](vbscript:msgbox(1))",
        "[x](VBScript:msgbox(1))",
        "[x](file:///etc/passwd)",
        "[x](//example.org/path)",
        "[x](/relative/path)",
        "[x](https://example.org \"onmouseover=alert(1)\")",
        "[x](https://example.org\"onclick=\"alert(1))",
        "![x](javascript:alert(1))",
        "![x](JAVASCRIPT:alert(1))",
        "![x](data:image/svg+xml,<svg onload=alert(1)>)",
        "![x](data:image/png;base64,AAAA)",
        "![x](https://example.org/a.png\"onerror=\"alert(1))",
        "![x\" onerror=\"alert(1)](https://example.org/a.png)",
        "<javascript:alert(1)>",
        "<data:text/html,x>",
        "<vbscript:msgbox(1)>",
        "see javascript:alert(1) and ftp://example.org/file and tel:+15550100",
        "[a [b](javascript:alert(1)) c](javascript:alert(2))",
        "[a [b](https://example.org) c](javascript:alert(2))",
        "[![x](javascript:alert(1))](javascript:alert(2))",
        "[![x](https://example.org/a.png)](javascript:alert(2))",
        "[![x](javascript:alert(1))](https://example.org)",
        "[x][bad]\n\n[bad]: javascript:alert(1)",
        "[bad]\n\n[bad]: data:text/html,<script>alert(1)</script>",
        "![x][bad]\n\n[bad]: vbscript:msgbox(1)",
        "`<script>alert(1)</script>`",
        "```\n<script>alert(1)</script>\n<img src=x onerror=alert(1)>\n```",
        "```html\" onclick=\"alert(1)\n<b>x</b>\n```",
        "```\n</code></pre><script>alert(1)</script>",
        "| a |\n| - |\n| <script>alert(1)</script> [x](javascript:alert(1)) |",
        "- [ ] <input type=\"text\" onfocus=\"alert(1)\" autofocus>",
        "> <details open ontoggle=\"alert(1)\">x</details>",
    ];

    #endregion

    #region Public Methods

    [Theory]
    [MemberData(nameof(HostileTexts))]
    public void Hostile_text_comes_out_harmless(string text)
    {
        var view = RenderMarkdown(text);

        ShouldBeHarmless(view);
        view.FindAll("img").Should().BeEmpty("no image loads until a person asks");

        LoadEveryImage(view);

        ShouldBeHarmless(view);
    }

    [Theory]
    [InlineData("`<script>alert(1)</script>`", "code")]
    [InlineData("```\n<script>alert(1)</script>\n```", "pre code")]
    public void Markup_inside_code_stays_text(string text, string selector)
    {
        var view = RenderMarkdown(text);

        view.Find(selector).TextContent.Should().Be("<script>alert(1)</script>");
    }

    [Fact]
    public void Raw_markup_in_a_message_is_shown_as_the_text_it_is()
    {
        var view = RenderMarkdown("before <b onclick=\"alert(1)\">bold</b> after");

        view.Find("p").TextContent.Should().Be("before <b onclick=\"alert(1)\">bold</b> after");
        view.FindAll("b").Should().BeEmpty();
    }

    [Theory]
    [InlineData("[site](https://example.org/page)", "https://example.org/page")]
    [InlineData("[site](http://example.org/)", "http://example.org/")]
    [InlineData("[write](mailto:crew@example.org)", "mailto:crew@example.org")]
    [InlineData("see https://example.org/page for more", "https://example.org/page")]
    public void Links_open_in_a_new_context_without_handing_over_the_opener(string text, string address)
    {
        var link = RenderMarkdown(text).Find("a");

        link.GetAttribute("href").Should().Be(address);
        link.GetAttribute("rel").Should().Be("noopener noreferrer");
        link.GetAttribute("target").Should().Be("_blank");
    }

    [Fact]
    public void A_link_to_a_refused_address_keeps_its_words_and_loses_the_link()
    {
        var view = RenderMarkdown("read [the notes](javascript:alert(1)) first");

        view.FindAll("a").Should().BeEmpty();
        view.Find("p").TextContent.Should().Be("read the notes first");
    }

    [Fact]
    public void A_remote_image_waits_behind_a_button_that_names_its_source_and_its_description()
    {
        var view = RenderMarkdown("![Crew diagram](https://example.org/crew.png)");

        view.FindAll("img").Should().BeEmpty();
        var button = view.Find("button");
        button.GetAttribute("type").Should().Be("button");
        button.TextContent.Should().Be("Load image from example.org: Crew diagram");
    }

    [Fact]
    public void A_remote_image_without_a_description_is_still_named_by_its_source()
    {
        var view = RenderMarkdown("![](https://example.org/crew.png)");

        view.Find("button").TextContent.Should().Be("Load image from example.org");
    }

    [Fact]
    public void Asking_for_a_remote_image_shows_it_with_dimensions_and_its_description()
    {
        var view = RenderMarkdown("![Crew diagram](https://example.org/crew.png)");

        view.Find("button").Click();

        view.FindAll("button").Should().BeEmpty();
        var image = view.Find("img");
        image.GetAttribute("src").Should().Be("https://example.org/crew.png");
        image.GetAttribute("alt").Should().Be("Crew diagram");
        image.GetAttribute("width").Should().MatchRegex("^[1-9][0-9]*$");
        image.GetAttribute("height").Should().MatchRegex("^[1-9][0-9]*$");
        image.GetAttribute("referrerpolicy").Should().Be("no-referrer");
    }

    [Fact]
    public void A_loaded_image_stays_loaded_while_more_text_streams_in()
    {
        var view = RenderMarkdown("![Crew diagram](https://example.org/crew.png)", streaming: true);
        view.Find("button").Click();

        view.Render(parameters => parameters.Add(
            component => component.Text,
            "![Crew diagram](https://example.org/crew.png)\n\nand ![another](https://example.net/other.png)"));

        view.FindAll("img").Should().ContainSingle().Which.GetAttribute("src").Should().Be("https://example.org/crew.png");
        view.FindAll("button").Should().ContainSingle().Which.TextContent.Should().Be("Load image from example.net: another");
    }

    [Fact]
    public void An_image_inside_a_link_is_not_a_control_inside_a_control()
    {
        var view = RenderMarkdown("[![Crew diagram](https://example.org/crew.png)](https://example.org/crew)");

        view.FindAll("a button").Should().BeEmpty();
        view.Find("button").TextContent.Should().Be("Load image from example.org: Crew diagram");
        view.Find("a").GetAttribute("href").Should().Be("https://example.org/crew");
        view.Find("a").TextContent.Should().Be("https://example.org/crew");
    }

    [Fact]
    public void An_unclosed_code_fence_in_streaming_text_is_a_code_block_to_the_end()
    {
        var view = RenderMarkdown("Run this:\n\n```bash\ndotnet build\ndotnet te", streaming: true);

        view.Find("pre code").TextContent.Should().Be("dotnet build\ndotnet te");
        view.Find(".dv-code-language").TextContent.Should().Be("bash");
    }

    [Fact]
    public void A_fenced_block_gets_the_copy_button()
    {
        var view = RenderMarkdown("```\ndotnet build\n```");

        view.Find(".dv-code button").TextContent.Trim().Should().Be("Copy code");
        view.FindComponent<CodeBlock>().Instance.Code.Should().Be("dotnet build");
    }

    [Fact]
    public void Changing_the_text_renders_the_new_text()
    {
        var view = RenderMarkdown("The first **wor", streaming: true);

        view.Render(parameters => parameters.Add(component => component.Text, "The first **words** arrived"));

        view.Find("p").TextContent.Should().Be("The first words arrived");
        view.Find("strong").TextContent.Should().Be("words");
    }

    [Fact]
    public void Streaming_text_is_marked_busy_and_finished_text_is_not()
    {
        var view = RenderMarkdown("Working", streaming: true);
        view.Find(".dv-markdown").GetAttribute("aria-busy").Should().Be("true");

        view.Render(parameters => parameters.Add(component => component.Streaming, false));

        view.Find(".dv-markdown").HasAttribute("aria-busy").Should().BeFalse();
    }

    [Fact]
    public void A_table_scrolls_inside_its_own_container_that_the_keyboard_can_reach()
    {
        var view = RenderMarkdown("| Bot | Tool |\n| --- | --- |\n| Scout | search |");

        var container = view.Find(".dv-markdown-table");
        container.GetAttribute("tabindex").Should().Be("0");
        container.GetAttribute("aria-label").Should().Be("Table");
        container.QuerySelectorAll("th").Select(cell => cell.TextContent).Should().Equal("Bot", "Tool");
        container.QuerySelectorAll("td").Select(cell => cell.TextContent).Should().Equal("Scout", "search");
    }

    [Fact]
    public void Task_list_items_are_read_only_checkboxes_with_a_name()
    {
        var boxes = RenderMarkdown("- [x] Parse\n- [ ] Render").FindAll("input");

        boxes.Should().HaveCount(2);
        boxes.Should().OnlyContain(box => box.GetAttribute("type") == "checkbox" && box.HasAttribute("disabled"));
        boxes.Select(box => box.GetAttribute("aria-label")).Should().Equal("Done", "Not done");
        boxes.Select(box => box.HasAttribute("checked")).Should().Equal(true, false);
    }

    [Fact]
    public void Common_formatting_becomes_the_matching_elements()
    {
        var view = RenderMarkdown("# Title\n\nSome *emphasis*, **strength**, ~~removal~~ and `code`.\n\n3. third\n4. fourth\n\n> quoted");

        view.Find("h1").TextContent.Should().Be("Title");
        view.Find("em").TextContent.Should().Be("emphasis");
        view.Find("strong").TextContent.Should().Be("strength");
        view.Find("del").TextContent.Should().Be("removal");
        view.Find("p code").TextContent.Should().Be("code");
        view.Find("ol").GetAttribute("start").Should().Be("3");
        view.FindAll("ol li").Should().HaveCount(2);
        view.Find("blockquote").TextContent.Trim().Should().Be("quoted");
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<MarkdownView> RenderMarkdown(string text, bool streaming = false) =>
        Render<MarkdownView>(parameters => parameters
            .Add(component => component.Text, text)
            .Add(component => component.Streaming, streaming));

    private static void LoadEveryImage(IRenderedComponent<MarkdownView> view)
    {
        while (view.FindAll("button.dv-markdown-image-load") is [var button, ..])
        {
            button.Click();
        }
    }

    private static void ShouldBeHarmless(IRenderedComponent<MarkdownView> view)
    {
        view.FindAll(ForbiddenElements).Should().BeEmpty();

        var attributes = view.FindAll("*").SelectMany(element => element.Attributes).ToList();
        attributes.Select(attribute => attribute.Name)
            .Should().NotContain(name => name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
        attributes.Where(attribute => attribute.Name is "href")
            .Should().OnlyContain(attribute => StartsWithAny(attribute.Value, allowedAddressStarts));
        attributes.Where(attribute => attribute.Name is "src")
            .Should().OnlyContain(attribute => StartsWithAny(attribute.Value, allowedImageStarts));
        attributes.Select(attribute => attribute.Name)
            .Should().NotContain(["action", "formaction", "data", "xlink:href", "srcset", "id", "name", "autofocus"]);
    }

    private static bool StartsWithAny(string value, string[] starts) =>
        starts.Any(start => value.StartsWith(start, StringComparison.Ordinal));

    #endregion
}
