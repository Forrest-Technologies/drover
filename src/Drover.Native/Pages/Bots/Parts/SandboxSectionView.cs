namespace Drover.Native.Pages;

/// <summary>
/// The Sandbox section of the bot editor: whether the bot has a place to run code and keep files, and who shares it.
/// </summary>
public sealed class SandboxSectionView : EditorPart
{
    #region Private Fields

    private readonly StatusNote provider = new();

    private readonly FlowPanel live = new() { Gap = Tokens.Number("DvSpace4") };

    private readonly TextBlock liveWords = Styles.Text(null).Inked("DvText2");

    private readonly Link openSandbox = new() { Text = SandboxSectionViewModel.OpenSandbox };

    private readonly Select policy;

    private readonly TextBlock meaning = BotParts.Hint();

    private readonly TextBlock noOtherBots = BotParts.Hint(SandboxSectionViewModel.NoOtherBots);

    private readonly NamedGroup share = new(SandboxSectionViewModel.ShareLegend, "DvSpace3");

    private readonly StackPanel shareBoxes = new();

    private readonly StackPanel settings = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly SegmentedControl network;

    private readonly TextBlock noOpenNetwork = BotParts.Hint(SandboxSectionViewModel.NoOpenNetwork);

    private readonly TextArea hosts;

    private readonly Field idle;

    private readonly Switch desktop;

    private readonly TextBlock desktopHint = BotParts.Hint(SandboxSectionViewModel.DesktopHint);

    private readonly StackPanel readBy;

    private readonly TextBlock readByWords = BotParts.Hint();

    private readonly TextBlock noDesktop = BotParts.Hint(SandboxSectionViewModel.NoDesktop);

    private readonly NamedGroup sandboxGrant;

    private readonly TextBlock sandboxGrantWords = BotParts.Hint();

    private readonly Button sandboxGrantAction = new();

    private readonly NamedGroup screenGrant;

    private readonly TextBlock screenGrantWords = BotParts.Hint();

    private readonly Button screenGrantAction = new();

    private readonly StackPanel cannotSee;

    private readonly Dictionary<Guid, CheckItem> boxes = [];

    #endregion

    #region Constructors

    public SandboxSectionView(BotEditorViewModel editor)
        : base(editor)
    {
        var part = editor.SandboxPart;
        liveWords.VerticalAlignment = VerticalAlignment.Center;
        openSandbox.MinHeight = Targets.Minimum;
        live.Children.Add(liveWords);
        live.Children.Add(openSandbox);
        policy = new Select { Label = SandboxSectionViewModel.PolicyLabel }.OnChanged(Quiet, value => Editor.Edit(draft => draft.SandboxPolicy = value));
        var legend = BotParts.ChoiceLabel(SandboxSectionViewModel.ShareLegend);
        share.Children.Add(legend);
        share.Children.Add(shareBoxes);
        network = new SegmentedControl { Label = SandboxSectionViewModel.NetworkLabel }.OnChanged(Quiet, value => Editor.Edit(draft => draft.Network = value));
        hosts = new TextArea { Label = SandboxSectionViewModel.HostsLabel, Machine = true, Rows = 3, Hint = SandboxSectionViewModel.HostsHint }
            .OnChanged(Quiet, value => Editor.Edit(draft => draft.AllowedHosts = value));
        idle = DraftField(SandboxSectionViewModel.IdleLabel, (draft, value) => draft.IdleTimeoutMinutes = value);
        desktop = new Switch { Label = SandboxSectionViewModel.DesktopLabel }.OnChanged(Quiet, on => Editor.Edit(draft => draft.Desktop = on));
        readBy = BotParts.Column("DvSpace2", readByWords, OpenSection(SandboxSectionViewModel.OpenModel, BotEditorViewModel.ModelSection));
        sandboxGrantAction.HorizontalAlignment = screenGrantAction.HorizontalAlignment = HorizontalAlignment.Left;
        sandboxGrantAction.Click += (_, _) => Act(part.SandboxStanding, part.AllowSandboxTools);
        screenGrantAction.Click += (_, _) => Act(part.ScreenStanding, part.AllowScreenTools);
        sandboxGrant = new NamedGroup(SandboxSectionViewModel.SandboxGroupName, "DvSpace2", sandboxGrantWords, sandboxGrantAction);
        screenGrant = new NamedGroup(SandboxSectionViewModel.ScreenGroupName, "DvSpace2", screenGrantWords, screenGrantAction, BotParts.Hint(SandboxSectionViewModel.TakeOver));
        cannotSee = BotParts.Column(
            "DvSpace2",
            new StatusNote { Text = SandboxSectionViewModel.CannotSee },
            OpenSection(SandboxSectionViewModel.OpenModel, BotEditorViewModel.ModelSection));

        foreach (var child in new UIElement[]
        {
            BotParts.Column("DvSpace1", BotParts.ChoiceLabel(SandboxSectionViewModel.NetworkWord), network), noOpenNetwork, hosts, idle, desktop, desktopHint,
            readBy, noDesktop, sandboxGrant, screenGrant, cannotSee,
        })
        {
            settings.Children.Add(child);
        }

        foreach (var child in new UIElement[] { provider, live, policy, meaning, noOtherBots, share, settings })
        {
            Parts.Add(child);
        }
    }

    #endregion

    #region Private Methods

    protected override void Update()
    {
        var part = Editor.SandboxPart;
        provider.Show(part.ProviderNote);
        live.Shown(part.Live is not null);
        liveWords.Show(part.Live);
        openSandbox.Address = part.SandboxAddress;
        policy.Show(SandboxSectionViewModel.Policies, Draft.SandboxPolicy);
        meaning.Show(part.Meaning);
        noOtherBots.Shown(part.SharingShown && part.OtherBots.Count == 0);
        share.Shown(part.SharingShown && part.OtherBots.Count > 0);
        ShowSharing(part);
        settings.Shown(part.SettingsShown);
        network.Show(part.Networks, Draft.Network);
        noOpenNetwork.Shown(part.NoOpenNetworkShown);
        hosts.Shown(part.HostsShown).Show(Draft.AllowedHosts);
        idle.Placeholder = part.IdlePlaceholder;
        idle.Show(Draft.IdleTimeoutMinutes);
        desktop.Shown(part.DesktopShown).Show(Draft.Desktop);
        desktopHint.Shown(part.DesktopShown);
        readBy.Shown(part.ReadByShown);

        if (part.ReadByShown)
        {
            readByWords.Show(part.ReadBy);
        }

        noDesktop.Shown(part.NoDesktopShown);
        sandboxGrant.Shown(part.SandboxGrantShown);
        sandboxGrantWords.Show(part.SandboxGrantWords);
        ShowAction(sandboxGrantAction, part.SandboxStanding, part.SandboxGrantAction);
        screenGrant.Shown(part.ScreenGrantShown);
        screenGrantWords.Show(part.ScreenGrantWords);
        ShowAction(screenGrantAction, part.ScreenStanding, part.ScreenGrantAction);
        cannotSee.Shown(part.CannotSeeShown);
    }

    private void ShowSharing(SandboxSectionViewModel part)
    {
        List<UIElement> shown = [];

        foreach (var bot in part.OtherBots)
        {
            if (!boxes.TryGetValue(bot.Id, out var box))
            {
                box = new CheckItem(bot.Name);
                box.Changed += shared => part.Share(bot.Id, shared);
                boxes[bot.Id] = box;
            }

            box.Show(part.SharesWith(bot.Id));
            shown.Add(box);
        }

        if (!shareBoxes.Children.SequenceEqual(shown))
        {
            shareBoxes.Fill(shown);
        }
    }

    // Allowing is the primary action; once allowed, the button only leads to the Tools section.
    private static void ShowAction(Button button, ToolStanding standing, string? label)
    {
        button.Shown(label is not null);
        button.Content = label;
        button.Kind = SandboxSectionViewModel.Allows(standing) ? ButtonKind.Primary : ButtonKind.Quiet;
    }

    private void Act(ToolStanding standing, Action allow)
    {
        if (SandboxSectionViewModel.Allows(standing))
        {
            allow();
        }
        else
        {
            Editor.Toggle(BotEditorViewModel.ToolsSection, true);
        }
    }

    private Button OpenSection(string label, string section) => BotParts.Action(label, () => Editor.Toggle(section, true));

    #endregion
}
