namespace Drover.Native.Shell;

/// <summary>
/// The place of a screen that has not moved to this app yet. It carries the screen's address and title, sits in
/// the screen's frame, and says plainly where the screen can be used today. Moving a screen here means replacing
/// the one small class in <c>Pages/</c> that derives from this; nothing shared changes.
/// </summary>
public abstract class PendingScreen(string name, ScreenLayout layout) : Screen
{
    #region Properties

    public override string Title => $"{name} - Drover";

    #endregion

    #region Private Methods

    protected override Task Opened(RouteMatch<Type> route)
    {
        var services = ((App)Application.Current).Services;
        var heading = Styles.Text(name, layout is ScreenLayout.Entry ? "DvTextH2" : "DvTextH1");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        var note = new StatusNote { Text = "This screen is not in this app yet. It works in the web app." };

        UIElement[] body = PlatformShell.WebAddress(services, route.Address) is { } address
            ? [heading, note, new Link { Text = $"Open {name} in the web app", Address = address }]
            : [heading, note];

        if (layout is ScreenLayout.Entry)
        {
            var entry = new EntryLayout();
            Add(entry.Body, body);
            Content = entry;
        }
        else
        {
            var work = new WorkLayout();
            Add(work.Body, body);
            Content = work;
        }

        return Task.CompletedTask;
    }

    private static void Add(UIElementCollection target, UIElement[] items)
    {
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    #endregion
}
