namespace Drover.Native.Pages;

/// <summary>
/// The conversations: the crew, the bot a person is talking to, and what is beside it. Every address of this
/// screen shows the same workspace, so moving between them keeps it as it is: the conversations on screen stay
/// where they were, with what was written and where the reader had scrolled to. Going to another screen ends it.
/// </summary>
public sealed class TalkPage : Screen
{
    #region Private Fields

    // The workspace on screen, and the page that shows it, while the app stays within this screen's addresses.
    private static (TalkPage Page, TalkViewModel Model, TalkWorkspace View, IServiceProvider Services)? live;

    #endregion

    #region Properties

    public override string Title => live?.Model.PageName ?? "Drover";

    /// <summary>A moment named after the hash, or a held request named in the query, changes nothing but what the workspace is told.</summary>
    protected override bool StaysOpenWhenQueryChanges => true;

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route)
    {
        await Take().Open(TalkAddress.From(route));
    }

    // Left for another screen, the workspace stops following its conversations and its signals. Left for another
    // address of this screen, it is kept for the page that address opens.
    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        if (e.SourcePageType != typeof(TalkPage) && live is { } left)
        {
            live = null;
            left.Page.ClearValue(ContentProperty);
            left.Model.Dispose();
        }
    }

    private TalkViewModel Take()
    {
        var services = ((App)Application.Current).Services;

        // An app that started over, on another server, has other services: what was kept belongs to the one before.
        if (live is { } stale && !ReferenceEquals(stale.Services, services))
        {
            live = null;
            stale.Page.ClearValue(ContentProperty);
            stale.Model.Dispose();
        }

        if (live is { } kept)
        {
            if (!ReferenceEquals(kept.Page, this))
            {
                kept.Page.ClearValue(ContentProperty);
                Content = kept.View;
                live = kept with { Page = this };
            }

            return kept.Model;
        }

        var model = ActivatorUtilities.CreateInstance<TalkViewModel>(services, new UiThread(DispatcherQueue));
        var view = new TalkWorkspace(model);
        view.TitleChanged += ShowTitle;
        Content = view;
        live = (this, model, view, services);
        return model;
    }

    private static void ShowTitle()
    {
        if (live is { } shown && ((App)Application.Current).MainWindow is { } window)
        {
            PlatformShell.SetTitle(window, shown.Model.PageName);
        }
    }

    #endregion
}
