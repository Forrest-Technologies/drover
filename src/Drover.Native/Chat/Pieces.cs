namespace Drover.Native.Chat;

/// <summary>
/// What the conversation's pieces share: how a panel makes its view model, the small parts their layouts repeat
/// (a part's title, a quiet line, a named fact, a link in a sentence), and how a line is said to a screen reader.
/// </summary>
internal static class Pieces
{
    #region Public Methods

    /// <summary>
    /// Makes a view model, filling its constructor from the app's services, as a screen's <c>Make</c> does. A panel
    /// is made by the conversation that shows it, not by the service provider, so this is its one use of it.
    /// </summary>
    public static T Make<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() =>
        ActivatorUtilities.CreateInstance<T>(((App)Application.Current).Services);

    /// <summary>
    /// Makes a view model that tells its control of changes from a clock or a device of its own: it is given the
    /// thread the control is drawn on.
    /// </summary>
    public static T Make<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(DependencyObject drawnOn) =>
        ActivatorUtilities.CreateInstance<T>(((App)Application.Current).Services, new UiThread(drawnOn.DispatcherQueue));

    /// <summary>The small heading of one part of a card: "Arguments", "What the bot was asked".</summary>
    public static TextBlock PartTitle(string text)
    {
        var title = Styles.Text(text, "DvTextLabel");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3);
        return title;
    }

    /// <summary>One part of a card: its title over what it holds.</summary>
    public static StackPanel Part(string title, params UIElement[] content)
    {
        var part = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        part.Children.Add(PartTitle(title));
        foreach (var element in content)
        {
            part.Children.Add(element);
        }

        return part;
    }

    /// <summary>A line in the secondary color, one size under the text around it.</summary>
    public static TextBlock Quiet(string? text)
    {
        var line = Styles.Text(text, "DvTextSecondary");
        line.FontSize = Tokens.Number("DvText13");
        line.LineHeight = Tokens.Number("DvLine13");
        return line;
    }

    /// <summary>What a call would do, one named fact to a row. A long value, such as instructions, keeps its lines.</summary>
    public static StackPanel Facts(IEnumerable<ToolFact> facts)
    {
        var list = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        foreach (var fact in facts)
        {
            var name = Styles.Text(fact.Name, "DvTextLabel");
            name.FontSize = Tokens.Number("DvText12");
            name.LineHeight = Tokens.Number("DvLine12");
            var row = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
            row.Children.Add(name);
            row.Children.Add(Styles.Text(fact.Value));
            list.Children.Add(row);
        }

        return list;
    }

    /// <summary>
    /// A status line: it is read out when it appears, without taking the focus, as a region marked as a status is on
    /// the web.
    /// </summary>
    public static void SayWhenShown(TextBlock line, bool atOnce = false)
    {
        AutomationProperties.SetLiveSetting(line, atOnce ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        line.Loaded += (_, _) => FrameworkElementAutomationPeer.FromElement(line)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>Words that take a person to an address inside the app, set within a sentence.</summary>
    public static Microsoft.UI.Xaml.Documents.Hyperlink LinkIn(FrameworkElement scope, string text, string address, string ink = "DvEmber")
    {
        var link = new Microsoft.UI.Xaml.Documents.Hyperlink { Foreground = Tokens.Brush(scope, ink) };
        link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text });
        link.Click += (_, _) => Link.Opener?.Invoke(address);
        return link;
    }

    /// <summary>
    /// Takes an element out of whatever holds it, so a piece that is kept while its card is drawn again (a box a
    /// person scrolled, a field they are typing in) can be put into the new drawing.
    /// </summary>
    public static T Detached<T>(T element)
        where T : FrameworkElement
    {
        switch (element.Parent ?? VisualTreeHelper.GetParent(element))
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Border border:
                border.Child = null;
                break;
            case ContentPresenter presenter:
                presenter.Content = null;
                break;
            case ContentControl control:
                control.Content = null;
                break;
        }

        return element;
    }

    /// <summary>Moves the focus to an element once it is on the screen: one just drawn cannot take it yet.</summary>
    public static void FocusWhenShown(FrameworkElement element, Action focus)
    {
        // An element just made visible has not been laid out yet either, so the focus moves after this pass.
        if (element.IsLoaded)
        {
            element.DispatcherQueue.TryEnqueue(() => focus());
            return;
        }

        RoutedEventHandler? shown = null;
        shown = (_, _) =>
        {
            element.Loaded -= shown;
            focus();
        };
        element.Loaded += shown;
    }

    /// <summary>True on a window as narrow as a phone, where the web client's layouts change shape.</summary>
    public static bool OnPhone(FrameworkElement element) =>
        element.XamlRoot is { Size.Width: > 0 } root && root.Size.Width < (double)Styles.Dictionary["DvBreakPhone"];

    /// <summary>Runs <paramref name="fit"/> when the element is shown and each time the window becomes, or stops being, as narrow as a phone.</summary>
    public static void WhenPhoneOrNot(FrameworkElement element, Action fit)
    {
        bool? last = null;

        void Check()
        {
            var phone = OnPhone(element);
            if (phone != last)
            {
                last = phone;
                fit();
            }
        }

        void Resized(XamlRoot root, XamlRootChangedEventArgs args) => Check();

        element.Loaded += (_, _) =>
        {
            element.XamlRoot!.Changed += Resized;
            Check();
        };
        element.Unloaded += (_, _) =>
        {
            if (element.XamlRoot is { } root)
            {
                root.Changed -= Resized;
            }
        };
    }

    #endregion
}
