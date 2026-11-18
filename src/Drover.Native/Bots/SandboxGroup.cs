namespace Drover.Native.Bots;

/// <summary>A row of controls that belong together and wrap as one, named as a group for assistive technology.</summary>
internal sealed class SandboxGroup : Grid
{
    #region Private Fields

    private readonly FlowPanel row = new();

    #endregion

    #region Constructors

    public SandboxGroup(string name, string gap)
    {
        row.Gap = Tokens.Number(gap);
        Children.Add(row);
        AutomationProperties.SetName(this, name);
    }

    #endregion

    #region Public Methods

    public void Add(params UIElement[] parts)
    {
        foreach (var part in parts)
        {
            row.Children.Add(part);
        }
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Group);

    #endregion
}
