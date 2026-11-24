namespace Drover.Native.Controls;

/// <summary>
/// The way round two faults in the tree Uno 7.0.65 builds for assistive technology in a browser.
/// <para>
/// One: a part that is collapsed and shown again can stay hidden there for good. Any rebuild of that tree while the
/// part is collapsed marks it as left out, and one runs each time an element that stands nowhere yet is given an
/// AccessibilityView, as every Icon is when it is made. Shown again, the part gets its node at once, hidden until it
/// is arranged; when it is arranged the mark is read as an order to build it, it is found built, and "hidden" is
/// never taken off. Where the part stands where it stood before, nothing later takes it off either.
/// </para>
/// <para>
/// Two: a node is placed when its own element is arranged, and a parent is arranged after what it holds. A node
/// under an element with no node of its own is placed as if that element had not moved, and is not placed again
/// when only that element moves.
/// </para>
/// The runtime takes a part's nodes out and puts them in anew, where they stand by then and without the mark, when
/// the part's AccessibilityView changes. So the view is changed and changed back.
/// </summary>
internal static class SemanticTree
{
    /// <summary>Has the browser's tree for assistive technology build the part's nodes again as they are now.</summary>
    public static void PutBack(UIElement part)
    {
        if (!OperatingSystem.IsBrowser())
        {
            return;
        }

        var view = AutomationProperties.GetAccessibilityView(part);
        var other = view is AccessibilityView.Raw ? AccessibilityView.Content : AccessibilityView.Raw;
        AutomationProperties.SetAccessibilityView(part, other);
        AutomationProperties.SetAccessibilityView(part, view);
    }
}
