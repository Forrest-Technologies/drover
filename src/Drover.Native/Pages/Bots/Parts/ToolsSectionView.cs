namespace Drover.Native.Pages;

/// <summary>The Tools section of the bot editor: every tool of the space, each off, ask, or allow for this bot.</summary>
public sealed class ToolsSectionView : EditorPart
{
    #region Private Fields

    private readonly StatusNote cannotCall = new() { Text = ToolsSectionViewModel.CannotCallTools };

    private readonly TextBlock loading = BotParts.Hint(ToolsSectionViewModel.LoadingTools);

    private readonly TextBlock hint = BotParts.Hint(ToolsSectionViewModel.Hint);

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly HiddenStatus announcement = new();

    private readonly StackPanel groups = new();

    private readonly StackPanel noConnections;

    private readonly Dictionary<string, OpenableGroup> groupViews = [];

    private readonly Dictionary<Guid, ToolRowView> rows = [];

    private readonly Dictionary<string, TextBlock> emptyHints = [];

    #endregion

    #region Constructors

    public ToolsSectionView(BotEditorViewModel editor)
        : base(editor)
    {
        AutomationProperties.SetLiveSetting(loading, AutomationLiveSetting.Polite);
        var add = new Link { Text = ToolsSectionViewModel.AddToolServer, Address = ToolsSectionViewModel.ToolServersAddress, FontSize = Tokens.Number("DvText13") };
        var line = new FlowPanel { Gap = Tokens.Number("DvSpace1") };
        line.Children.Add(BotParts.Hint(ToolsSectionViewModel.NoConnectionsBefore));
        line.Children.Add(add);
        line.Children.Add(BotParts.Hint(ToolsSectionViewModel.NoConnectionsAfter));
        noConnections = BotParts.Column("DvSpace1", line);

        foreach (var child in new UIElement[] { cannotCall, loading, hint, problem, groups, noConnections })
        {
            Parts.Add(child);
        }

        Beside(announcement);
    }

    #endregion

    #region Private Methods

    protected override void Update()
    {
        var part = Editor.ToolsPart;
        var listed = part.ModelCallsTools && !part.Loading;
        cannotCall.Shown(!part.ModelCallsTools);
        loading.Shown(part.ModelCallsTools && part.Loading);

        foreach (var shown in new UIElement[] { hint, problem, groups })
        {
            shown.Shown(listed);
        }

        noConnections.Shown(listed && part.NoConnectionsShown);

        if (listed)
        {
            problem.Show(part.Problem);
            announcement.Say(part.Announcement);
            ShowGroups(part);
        }
    }

    // Built-in tools first, then a group for each connection; each group keeps whether it is open while it stays.
    private void ShowGroups(ToolsSectionViewModel part)
    {
        List<UIElement> shown = [];

        foreach (var group in part.Groups)
        {
            if (!groupViews.TryGetValue(group.Name, out var view))
            {
                view = new OpenableGroup(open: group.ConnectionId is null);
                groupViews[group.Name] = view;
            }

            view.Show(group.Name, ToolsSectionViewModel.Count(group));
            List<UIElement> body = [];

            if (group.Tools.Count == 0)
            {
                if (!emptyHints.TryGetValue(group.Name, out var empty))
                {
                    empty = BotParts.Hint();
                    emptyHints[group.Name] = empty;
                }

                empty.Show(ToolsSectionViewModel.Empty(group));
                body.Add(empty);
            }

            foreach (var tool in group.Tools)
            {
                if (!rows.TryGetValue(tool.ToolId, out var row))
                {
                    row = new ToolRowView(part, tool);
                    rows[tool.ToolId] = row;
                }

                row.Show(tool, group);
                body.Add(row);
            }

            if (!view.Body.SequenceEqual(body))
            {
                view.Body.Clear();
                foreach (var item in body)
                {
                    (VisualTreeHelper.GetParent(item) as Panel)?.Children.Remove(item);
                    view.Body.Add(item);
                }
            }

            shown.Add(view);
        }

        if (!groups.Children.SequenceEqual(shown))
        {
            groups.Fill(shown);
        }
    }

    #endregion
}
