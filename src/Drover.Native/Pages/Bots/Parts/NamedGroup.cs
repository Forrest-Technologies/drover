namespace Drover.Native.Pages;

/// <summary>
/// A column of controls that belong together under one name, the web's fieldset with its legend or an element with
/// the group role: assistive technology reads the name before the controls in it.
/// </summary>
public class NamedGroup : StackPanel
{
    #region Constructors

    public NamedGroup()
        : this(string.Empty, null)
    {
    }

    public NamedGroup(string name, string? gap, params UIElement[] children)
    {
        Spacing = gap is null ? 0 : Tokens.Number(gap);
        Describe(name);
        foreach (var child in children)
        {
            Children.Add(child);
        }
    }

    #endregion

    #region Properties

    /// <summary>What the column is to assistive technology: a group, or a list of things of one kind.</summary>
    public AutomationControlType Role { get; set; } = AutomationControlType.Group;

    #endregion

    #region Public Methods

    public void Describe(string name) => AutomationProperties.SetName(this, name);

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, Role);

    #endregion
}
