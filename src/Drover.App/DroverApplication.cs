namespace Drover.App;

public sealed class DroverApplication : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new() { Selector = "#app", ComponentType = typeof(PreviewRoot) });
        view.RootComponents.Add(new() { Selector = "head::after", ComponentType = typeof(HeadOutlet) });
        return new(new ContentPage { Content = view, SafeAreaEdges = SafeAreaEdges.Container });
    }
}
