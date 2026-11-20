namespace Drover.Native.Chat;

/// <summary>
/// Keeps a panel's children in step with a list of things that each have a key: a message, a tool call, a file.
/// A child is kept for as long as its key is listed and is only told what changed, so its own state (an open
/// disclosure, the focus, a fade under way) outlives every change around it. New children are made, children whose
/// key is gone are dropped, and the order follows the list.
/// </summary>
internal static class Keyed
{
    /// <summary>
    /// Brings <paramref name="panel"/>'s children from <paramref name="start"/> on in step with <paramref name="items"/>.
    /// Children before <paramref name="start"/> and the last <paramref name="tail"/> children are the caller's own and stay.
    /// </summary>
    public static void Sync<TItem, TChild>(
        Panel panel,
        IReadOnlyList<TItem> items,
        Func<TItem, string> key,
        Func<TItem, TChild> make,
        Action<TChild, TItem> show,
        int start = 0,
        int tail = 0)
        where TChild : FrameworkElement
    {
        var end = panel.Children.Count - tail;
        Dictionary<string, TChild> kept = [];
        for (var index = start; index < end; index++)
        {
            if (panel.Children[index] is TChild { Tag: string name } child)
            {
                kept[name] = child;
            }
        }

        var unchanged = end - start == items.Count;
        for (var index = 0; unchanged && index < items.Count; index++)
        {
            unchanged = panel.Children[start + index] is TChild { Tag: string name } && name == key(items[index]);
        }

        if (!unchanged)
        {
            Place(panel, items, key, make, kept, start, tail);
        }

        for (var index = 0; index < items.Count; index++)
        {
            show((TChild)panel.Children[start + index], items[index]);
        }
    }

    // What is still listed stays where it is: children whose key is gone are taken out, a new child is put in at its
    // place, and only a kept child that the list moved is taken out and put back. A child that stays in place keeps
    // the focus, so a list that grows at its end, or gains an item in its middle, never takes it from the person.
    private static void Place<TItem, TChild>(
        Panel panel,
        IReadOnlyList<TItem> items,
        Func<TItem, string> key,
        Func<TItem, TChild> make,
        Dictionary<string, TChild> kept,
        int start,
        int tail)
        where TChild : FrameworkElement
    {
        HashSet<string> listed = [.. items.Select(key)];
        for (var index = panel.Children.Count - tail - 1; index >= start; index--)
        {
            if (panel.Children[index] is not TChild { Tag: string name } || !listed.Contains(name))
            {
                panel.Children.RemoveAt(index);
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            var name = key(items[index]);
            var at = start + index;
            if (at < panel.Children.Count - tail && panel.Children[at] is TChild { Tag: string placed } && placed == name)
            {
                continue;
            }

            if (kept.TryGetValue(name, out var child))
            {
                panel.Children.Remove(child);
            }
            else
            {
                child = make(items[index]);
                child.Tag = name;
            }

            panel.Children.Insert(at, child);
        }
    }
}
