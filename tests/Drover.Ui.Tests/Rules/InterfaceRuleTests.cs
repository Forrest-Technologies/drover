namespace Drover.Ui.Tests.Rules;

/// <summary>
/// The rules DESIGN.md marks as tested. Each rule is shown to fail on a bad sample and pass on the fix,
/// and then the files that ship are scanned.
/// </summary>
public sealed class InterfaceRuleTests
{
    #region Private Fields

    private const string StyleFile = "src/Drover.Ui/Components/Sample.razor.css";

    private const string MarkupFile = "src/Drover.Ui/Components/Sample.razor";

    #endregion

    #region The Files That Ship

    [Fact]
    public void The_interface_files_break_no_rule()
    {
        List<ScanResult> results =
        [
            .. RepositoryFiles.InterfaceFiles()
                .Select(path => InterfaceRules.Scan(RepositoryFiles.Relative(path), File.ReadAllText(path))),
        ];

        foreach (var allowance in results.SelectMany(result => result.Allowances))
        {
            Xunit.TestContext.Current.TestOutputHelper?.WriteLine($"Allowed: {allowance}");
        }

        results.Should().NotBeEmpty("the scan must find the interface files");
        List<Violation> violations = [.. results.SelectMany(result => result.Violations)];
        if (violations.Count > 0)
        {
            var listed = string.Join(Environment.NewLine, violations);
            Assert.Fail($"{violations.Count} places break a rule in DESIGN.md:{Environment.NewLine}{listed}");
        }
    }

    #endregion

    #region No Raw Values

    [Fact]
    public void A_raw_color_size_or_duration_in_a_style_sheet_fails()
    {
        var bad = """
            .card {
              color: #FFB454;
              background: rgb(10 12 16 / 0.5);
              border-color: white;
              padding: 12px 1.5rem;
              transition: opacity 120ms;
              margin: var(--dv-space-2, 8px);
            }
            """;
        var good = """
            .card {
              color: var(--dv-ember);
              background: color-mix(in oklab, var(--dv-bg-0) 50%, transparent);
              border-color: currentColor;
              padding: var(--dv-space-3) 0;
              margin: 0px auto;
              transition: opacity var(--dv-dur-1);
              width: calc(100% - var(--dv-space-4));
              max-width: 65ch;
              min-height: 50dvh;
              grid-template-columns: 1fr 2fr;
              opacity: 0.35;
              line-height: 1.5;
              transform: rotate(45deg) translateX(-100%);
              background-image: url("images/grid-16px.svg#a1b2c3");
              content: "12px #FFF 2s";
            }

            @keyframes arrive {
              0% {
                opacity: 0;
              }

              100% {
                opacity: 1;
              }
            }

            @media (min-width: 600px) {
              .card {
                padding: var(--dv-space-5);
              }
            }
            """;

        Found(StyleFile, bad).Should().Equal(
            (RuleNames.NoRawValues, 2),
            (RuleNames.NoRawValues, 3),
            (RuleNames.NoRawValues, 4),
            (RuleNames.NoRawValues, 5),
            (RuleNames.NoRawValues, 5),
            (RuleNames.NoRawValues, 6),
            (RuleNames.NoRawValues, 7));
        Found(StyleFile, good).Should().BeEmpty();
    }

    [Fact]
    public void A_raw_value_in_markup_fails_and_svg_geometry_does_not()
    {
        var bad = """
            <p style="margin-top: 4px; color: #fff">Ready</p>
            <svg viewBox="0 0 16 16" width="16" height="16"><path d="M2 8h12" stroke="#67E8F9" /></svg>
            <style>
                .note { font-size: 13px; }
            </style>
            """;
        var good = """
            <p style="margin-top: var(--dv-space-1); color: var(--dv-text-2)">Ready</p>
            <svg viewBox="0 0 16 16" width="16" height="16" stroke-width="1.5">
                <path d="M2 8h12" stroke="currentColor" fill="none" />
                <circle cx="8" cy="8" r="6.5" stroke="var(--dv-ion)" fill="@Fill" />
            </svg>
            <style>
                .note { font-size: var(--dv-text-13); }
            </style>
            """;

        Found(MarkupFile, bad).Should().Equal(
            (RuleNames.NoRawValues, 1),
            (RuleNames.NoRawValues, 1),
            (RuleNames.NoRawValues, 2),
            (RuleNames.NoRawValues, 4));
        Found(MarkupFile, good).Should().BeEmpty();
    }

    [Fact]
    public void The_tokens_file_is_where_raw_values_live()
    {
        var tokens = """
            :root {
              --dv-ember: #FFB454;
              --dv-space-1: 4px;
              --dv-dur-1: 120ms;
            }
            """;

        Found("src/Drover.Ui/wwwroot/tokens.css", tokens).Should().BeEmpty();
        Found("src/Drover.Ui/wwwroot/base.css", tokens).Should().HaveCount(3);
    }

    #endregion

    #region No Emoji

    [Fact]
    public void An_emoji_in_markup_or_styles_fails()
    {
        var rocket = char.ConvertFromUtf32(0x1F680);
        var checkMark = char.ConvertFromUtf32(0x2705);

        Found(MarkupFile, $"<h1>Crew</h1>\n<p>Launch {rocket}</p>").Should().Equal((RuleNames.NoEmoji, 2));
        Found(StyleFile, $".done::after {{\n  content: \"{checkMark}\";\n}}").Should().Equal((RuleNames.NoEmoji, 2));
        Found(MarkupFile, "<h1>Crew</h1>\n<p>Launch → 4 × 2 ©</p>").Should().BeEmpty();
        Found(StyleFile, ".done::after {\n  content: \"→\";\n}").Should().BeEmpty();
    }

    #endregion

    #region Motion

    [Fact]
    public void A_looping_animation_outside_the_four_live_states_fails()
    {
        var bad = """
            .badge {
              animation: dv-breath var(--dv-dur-breath) infinite;
            }

            .dv-state-thinking,
            .spinner {
              animation-iteration-count: infinite;
            }
            """;
        var good = """
            .dv-state-thinking,
            .avatar.dv-state-tool::before {
              animation: dv-breath var(--dv-dur-breath) infinite;
            }

            .sheet {
              animation: dv-arrive var(--dv-dur-2) var(--dv-ease);
            }

            @media (prefers-reduced-motion: reduce) {
              .dv-state-thinking,
              .avatar.dv-state-tool::before {
                animation: none;
              }
            }
            """;

        Found(StyleFile, bad).Should().Contain(
            [(RuleNames.MotionOnlyForLiveState, 2), (RuleNames.MotionOnlyForLiveState, 7)]);
        Found(StyleFile, good).Should().BeEmpty();
    }

    [Fact]
    public void A_live_state_that_animates_with_no_reduced_motion_rule_fails()
    {
        var bad = """
            .dv-state-thinking {
              animation: dv-breath var(--dv-dur-breath) infinite;
            }

            .dv-state-tool::before {
              animation-name: dv-travel;
            }

            @media (prefers-reduced-motion: reduce) {
              .dv-state-thinking {
                animation: none;
              }
            }
            """;
        var good = """
            .dv-state-thinking {
              animation: dv-breath var(--dv-dur-breath) infinite;
            }

            .dv-state-tool::before {
              animation-name: dv-travel;
            }

            @media (prefers-reduced-motion: no-preference) {
              .dv-state-delegation {
                animation: dv-pulse var(--dv-dur-3) 2 alternate;
              }
            }

            @media (prefers-reduced-motion: reduce) {
              .dv-state-thinking,
              .dv-state-tool::before {
                animation: none;
              }
            }
            """;

        Found(StyleFile, bad).Should().Equal((RuleNames.ReducedMotionHonored, 6));
        Found(StyleFile, good).Should().BeEmpty();
    }

    #endregion

    #region Layout

    [Fact]
    public void A_height_in_plain_viewport_units_fails()
    {
        var bad = """
            .shell {
              height: 100vh;
              min-height: calc(100vh - var(--dv-space-7));
              max-height: 50vh;
            }
            """;
        var good = """
            .shell {
              height: 100dvh;
              min-height: calc(100svh - var(--dv-space-7));
              max-height: 50lvh;
              width: 100vw;
            }
            """;

        Found(StyleFile, bad).Should().Equal(
            (RuleNames.NoViewportHeight, 2),
            (RuleNames.NoViewportHeight, 3),
            (RuleNames.NoViewportHeight, 4));
        Found(StyleFile, good).Should().BeEmpty();
    }

    [Fact]
    public void Media_without_both_dimensions_fails()
    {
        var bad = """
            <img src="@Avatar" alt="" />
            <img src="@Avatar" alt="" width="32" />
            <iframe src="@Preview" title="Preview"></iframe>
            """;
        var good = """
            <img src="@Avatar" alt="" width="32" height="32" />
            <iframe src="@Preview" title="Preview" width="@Width" height="@Height"></iframe>
            """;

        Found(MarkupFile, bad).Should().Equal(
            (RuleNames.MediaHasDimensions, 1),
            (RuleNames.MediaHasDimensions, 2),
            (RuleNames.MediaHasDimensions, 3));
        Found(MarkupFile, good).Should().BeEmpty();
    }

    #endregion

    #region Controls

    [Fact]
    public void Removing_the_focus_outline_with_no_replacement_fails()
    {
        var bad = """
            .field:focus {
              outline: none;
            }

            .chip:focus-visible {
              outline: 0;
              color: var(--dv-ember);
            }
            """;
        var good = """
            .field:focus-visible {
              outline: none;
              box-shadow: 0 0 0 var(--dv-focus-width) var(--dv-ember);
            }

            .chip:focus-visible {
              outline: 0;
              border-color: var(--dv-ember);
            }

            .link:focus-visible {
              outline: var(--dv-focus-width) solid var(--dv-ember);
            }
            """;

        Found(StyleFile, bad).Should().Equal((RuleNames.FocusAlwaysVisible, 2), (RuleNames.FocusAlwaysVisible, 6));
        Found(StyleFile, good).Should().BeEmpty();
    }

    [Fact]
    public void A_click_handler_on_a_div_or_span_fails()
    {
        var bad = """
            <div class="row" @onclick="Open">Open the thread</div>
            <span
                @onclick="@(() => Select(item))">@item.Name</span>
            """;
        var good = """
            <button type="button" class="row" @onclick="Open">Open the thread</button>
            <a href="@item.Url" @onclick="@(() => Select(item))">@item.Name</a>
            <div class="row"><span>@item.Name</span></div>
            """;

        Found(MarkupFile, bad).Should().Equal(
            (RuleNames.ControlsAreRealControls, 1),
            (RuleNames.ControlsAreRealControls, 2));
        Found(MarkupFile, good).Should().BeEmpty();
    }

    [Fact]
    public void An_icon_button_with_no_label_fails()
    {
        var bad = """
            <IconButton Icon="Icons.Close" OnClick="Close" />
            <IconButton Icon="Icons.Close" Label="" />
            """;
        var good = """
            <IconButton Icon="Icons.Close" Label="Close" OnClick="Close" />
            <IconButton Icon="Icons.Menu" Label="@menuLabel"></IconButton>
            """;

        Found(MarkupFile, bad).Should().Equal(
            (RuleNames.IconOnlyControlsAreNamed, 1),
            (RuleNames.IconOnlyControlsAreNamed, 2));
        Found(MarkupFile, good).Should().BeEmpty();
    }

    [Fact]
    public void A_field_with_no_label_fails()
    {
        var bad = """
            <input type="text" placeholder="Name" />
            <label for="other">Model</label>
            <select id="model"></select>
            <label>Notes</label>
            <textarea></textarea>
            """;
        var good = """
            <label>Name <input type="text" /></label>
            <label for="model">Model</label>
            <select id="model"></select>
            <label for="@notesId">Notes</label>
            <textarea id="@notesId"></textarea>
            <input type="search" aria-label="Search threads" />
            <input type="text" aria-labelledby="heading" />
            <input type="hidden" name="token" value="@token" />
            <input type="submit" value="Save bot" />
            """;

        Found(MarkupFile, bad).Should().Equal(
            (RuleNames.FieldsAreLabeled, 1),
            (RuleNames.FieldsAreLabeled, 3),
            (RuleNames.FieldsAreLabeled, 5));
        Found(MarkupFile, good).Should().BeEmpty();
    }

    #endregion

    #region Words

    [Fact]
    public void A_hype_word_or_an_exclamation_mark_in_interface_text_fails()
    {
        var bad = """
            <h1>A powerful crew</h1>
            <p>Seamless, AI-powered and next-gen. Welcome!</p>
            <Button Title="Supercharge your day">Connect calendar</Button>
            <input aria-label="Magic search" placeholder="Search!" />
            """;
        var good = """
            @page "/crew"
            @* A powerful comment is not interface text! *@
            <!-- Neither is this one! -->
            <h1>Crew</h1>
            @if (!loaded && count != 0)
            {
                <p class="note">Loading the crew.</p>
            }
            <Button Title="Connect calendar" Disabled="@(!ready)" OnClick="@(() => Save(bot!))">Connect</Button>
            <input aria-label="@label!" placeholder="Search threads" />
            <style>
                .note { margin: 0 !important; }
            </style>

            @code {
                private bool loaded = !string.IsNullOrEmpty("Magic!");
            }
            """;

        Found(MarkupFile, bad).Should().Equal(
            (RuleNames.NoHypeWords, 1),
            (RuleNames.NoHypeWords, 2),
            (RuleNames.NoHypeWords, 2),
            (RuleNames.NoHypeWords, 2),
            (RuleNames.NoHypeWords, 2),
            (RuleNames.NoHypeWords, 3),
            (RuleNames.NoHypeWords, 4),
            (RuleNames.NoHypeWords, 4));
        Found(MarkupFile, good).Should().BeEmpty();
    }

    #endregion

    #region Allow Comments

    [Fact]
    public void An_allow_comment_with_a_reason_lifts_one_rule_on_the_next_line_and_is_reported()
    {
        var markup = """
            @* design: allow controls-are-real-controls because the backdrop closes the sheet and Escape does too *@
            <div class="backdrop" @onclick="Close"></div>
            <div class="row" @onclick="Open">Open the thread</div>
            """;
        var styles = """
            .rule {
              /* design: allow no-raw-values because a hairline must stay one device pixel */
              height: 1px;
              width: 1px; /* design: allow no-raw-values because it is on the same line */
              margin: 1px;
            }
            """;

        var markupResult = InterfaceRules.Scan(MarkupFile, markup);
        var styleResult = InterfaceRules.Scan(StyleFile, styles);

        markupResult.Violations.Select(violation => (violation.Rule, violation.Line))
            .Should().Equal((RuleNames.ControlsAreRealControls, 3));
        markupResult.Allowances.Should().Equal(new Allowance(
            RuleNames.ControlsAreRealControls,
            MarkupFile,
            1,
            2,
            "the backdrop closes the sheet and Escape does too"));
        styleResult.Violations.Select(violation => (violation.Rule, violation.Line))
            .Should().Equal((RuleNames.NoRawValues, 5));
        styleResult.Allowances.Select(allowance => allowance.Line).Should().Equal(2, 4);
    }

    [Fact]
    public void An_allow_comment_lifts_only_the_rule_it_names()
    {
        var markup = """
            @* design: allow no-hype-words because the product name is quoted from the vendor *@
            <div @onclick="Open">Magic Mirror</div>
            """;

        Found(MarkupFile, markup).Should().Equal((RuleNames.ControlsAreRealControls, 2));
    }

    [Fact]
    public void An_allow_comment_needs_a_reason_and_a_real_rule_and_something_to_allow()
    {
        var noReason = """
            @* design: allow controls-are-real-controls *@
            <div @onclick="Close"></div>
            """;
        var noRule = """
            .rule {
              /* design: allow anything because it is easier */
              height: 1px;
            }
            """;
        var nothingToAllow = """
            @* design: allow media-has-dimensions because the image was replaced *@
            <p>Ready</p>
            """;

        Found(MarkupFile, noReason).Should().Equal((RuleNames.AllowComment, 1), (RuleNames.ControlsAreRealControls, 2));
        Found(StyleFile, noRule).Should().Equal((RuleNames.AllowComment, 2), (RuleNames.NoRawValues, 3));
        Found(MarkupFile, nothingToAllow).Should().Equal((RuleNames.AllowComment, 1));
    }

    #endregion

    #region Private Methods

    private static IEnumerable<(string Rule, int Line)> Found(string file, string text) =>
        InterfaceRules.Scan(file, text).Violations.Select(violation => (violation.Rule, violation.Line));

    #endregion
}
