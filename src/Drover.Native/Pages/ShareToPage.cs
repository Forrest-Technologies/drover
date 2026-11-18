namespace Drover.Native.Pages;

/// <summary>
/// Choose which bot's conversation gets what another app shared, as a draft. Nothing is sent until the person sends
/// it: the share waits in the one inbox, and the chosen bot's main conversation takes it when it opens.
/// </summary>
public sealed class ShareToPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace4"), Padding = Tokens.Space("DvSpace4", "DvSpace5") };

    private readonly ShareToViewModel model = Make<ShareToViewModel>();

    #endregion

    #region Constructors

    public ShareToPage()
    {
        // The web's column: as wide as an entry screen, centered, never wider than the window.
        column.MaxWidth = Tokens.Number("DvEntryWidth");
        column.HorizontalAlignment = HorizontalAlignment.Center;
        layout.Body.Add(column);
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => ShareToViewModel.Title;

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) => await While(model.Open());

    private async Task While(Task work)
    {
        Refresh();
        try
        {
            await work;
        }
        finally
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var parts = column.Children;
        parts.Clear();
        var title = Styles.Text(ShareToViewModel.Heading, "DvTextH1");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        parts.Add(title);
        if (model.ShowNothing)
        {
            parts.Add(new EmptyState { Cause = ShareToViewModel.NothingCause, ActionLabel = ShareToViewModel.GoToCrewLabel, ActionHref = Addresses.Home });
            return;
        }

        parts.Add(WhatWasShared());
        if (model.ShowFailed)
        {
            var failed = new EmptyState { Title = ShareToViewModel.FailedTitle, Cause = model.Problem ?? string.Empty, ActionLabel = ShareToViewModel.TryAgain };
            failed.Action += async (_, _) => await While(model.Load());
            parts.Add(failed);
        }
        else if (model.ShowLoading)
        {
            parts.Add(PageParts.Waiting(ShareToViewModel.LoadingNote));
        }
        else if (model.ShowNoBots)
        {
            parts.Add(new EmptyState { Cause = ShareToViewModel.NoBotsCause, ActionLabel = ShareToViewModel.GoToCrewLabel, ActionHref = Addresses.Home });
        }
        else
        {
            parts.Add(Styles.Text(ShareToViewModel.Ask, "DvTextH2"));
            parts.Add(Styles.Text(ShareToViewModel.Note, "DvTextSecondary"));
            parts.Add(Choices());
        }

        var discard = new Button { Content = new TextBlock { Text = ShareToViewModel.DiscardLabel, TextWrapping = TextWrapping.Wrap }, Command = model.DiscardCommand, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(discard, ShareToViewModel.DiscardLabel);
        parts.Add(discard);
    }

    private Facet WhatWasShared()
    {
        var frame = new Facet { Padding = new(Tokens.Number("DvSpace4")) };
        var what = new NamedGroup(ShareToViewModel.WhatYouSharedLabel, "DvSpace3");
        if (model.HasText)
        {
            var text = Styles.Text(model.Excerpt, "DvTextParagraph");
            text.IsTextSelectionEnabled = true;
            what.Children.Add(text);
        }

        if (model.HasFiles)
        {
            var files = new NamedGroup(string.Empty, "DvSpace2") { Role = AutomationControlType.List };
            foreach (var (name, size) in model.Files)
            {
                var line = new FlowPanel { Gap = Tokens.Number("DvSpace1") };
                line.Children.Add(Styles.Text(name));
                line.Children.Add(Styles.Text(size, "DvTextMono"));
                files.Children.Add(line);
            }

            what.Children.Add(files);
        }

        frame.Children.Add(what);
        return frame;
    }

    private NamedGroup Choices()
    {
        var list = new NamedGroup(ShareToViewModel.Ask, "DvSpace2") { Role = AutomationControlType.List };
        foreach (var choice in model.Choices)
        {
            var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace3") };
            var avatar = new Avatar { BotName = choice.Name, Hue = choice.Hue, Size = 32 };
            AutomationProperties.SetAccessibilityView(avatar, AccessibilityView.Raw);
            face.Children.Add(avatar);
            face.Children.Add(new TextBlock { Text = choice.Name, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var button = new BotChoiceButton { Content = face, Command = choice.ChooseCommand };
            AutomationProperties.SetName(button, choice.Name);
            list.Children.Add(button);
        }

        return list;
    }

    #endregion
}
