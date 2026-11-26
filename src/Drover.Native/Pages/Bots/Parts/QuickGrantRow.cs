namespace Drover.Native.Pages;

/// <summary>
/// The first row of a group in the Tools section: one control that sets every tool in it at once, off, read only,
/// ask, or allow, laid out as a tool's own row is. It shows the setting the tools are at, and none when they differ.
/// </summary>
public sealed class QuickGrantRow : Border
{
    #region Private Fields

    private readonly TextBlock label = BotParts.ChoiceLabel(ToolsSectionViewModel.QuickLabel);

    private readonly TextBlock hint = BotParts.Hint();

    private readonly SegmentedControl grant = new();

    private ToolsSectionViewModel.ToolGroup group;

    private bool showing;

    #endregion

    #region Constructors

    public QuickGrantRow(ToolsSectionViewModel part, ToolsSectionViewModel.ToolGroup group)
    {
        this.group = group;
        label.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        grant.OnChanged(() => showing, value => _ = part.SetAll(this.group, value));
        var row = new WrapRow();
        row.Children.Add(BotParts.Column("DvSpace1", label, hint));
        row.Children.Add(grant);
        Child = row;
        Padding = new(0, Tokens.Number("DvSpace1"), 0, Tokens.Number("DvSpace3"));
    }

    #endregion

    #region Public Methods

    public void Show(ToolsSectionViewModel part, ToolsSectionViewModel.ToolGroup shown)
    {
        showing = true;
        group = shown;
        grant.Label = part.QuickGrantLabel(shown);
        grant.Show(ToolsSectionViewModel.QuickOptions(shown), ToolsSectionViewModel.QuickValue(shown));
        grant.Disabled = part.BusyGroup == shown.Name || part.Busy is not null;
        hint.Shown(ToolsSectionViewModel.QuickHint(shown) is not null).Show(ToolsSectionViewModel.QuickHint(shown));
        showing = false;
    }

    #endregion
}
