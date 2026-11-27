namespace Drover.Ui.Components;

/// <summary>
/// The part of a long list that is on the page: a run of neighbouring items around where the reader is. The list
/// itself is never cut: every item stays in it, and the window moves over it as the reader goes back, comes forward,
/// or lands on one item from a link. What a screen costs to show then follows what is on the page, not how long the
/// list has grown. The window is held by the keys of its ends, so items arriving, changing, or leaving around it do
/// not move it; it follows the end of the list while it reaches it.
/// </summary>
/// <param name="key">What names an item for as long as it is listed.</param>
/// <param name="opens">
/// True for an item that begins a group shown as one, such as the first message of a turn. The window never starts
/// or ends inside a group. Without it every item stands alone.
/// </param>
public sealed class ListWindow<T>(Func<T, string> key, Func<T, bool>? opens = null)
{
    #region Private Fields

    private IReadOnlyList<T> items = [];

    private int first;

    private int count;

    private string? firstKey;

    private string? lastKey;

    #endregion

    #region Properties

    /// <summary>How many items are shown at first, and around an item landed on.</summary>
    public int Page { get; init; } = 30;

    /// <summary>How many more items one step back or forward shows.</summary>
    public int Step { get; init; } = 10;

    /// <summary>The most items kept on the page. Past it, the end furthest from where the page grew lets go.</summary>
    public int Most { get; init; } = 200;

    /// <summary>The items on the page, in the list's order.</summary>
    public IReadOnlyList<T> Shown { get; private set; } = [];

    /// <summary>How many items of the list come before the ones on the page.</summary>
    public int Earlier => first;

    /// <summary>How many items of the list come after the ones on the page.</summary>
    public int Later => items.Count - first - count;

    #endregion

    #region Public Methods

    /// <summary>
    /// Takes the list as it is now. The window stays on the items it held; while it reached the end of the list it
    /// goes on reaching it, so what arrives is shown. A list whose held items are gone is shown from its end.
    /// </summary>
    public void Show(IReadOnlyList<T> all)
    {
        var followed = Later == 0;
        var held = count;
        items = all;
        var start = Find(firstKey, first);

        if (start < 0)
        {
            Set(all.Count - Page, all.Count - 1, keepsEnd: true);
            return;
        }

        var end = followed ? all.Count - 1 : Find(lastKey, start + held - 1);
        Set(start, end < start ? start + held - 1 : end, keepsEnd: followed);
    }

    /// <summary>Shows a step more of what comes before. False when nothing does.</summary>
    public bool ShowEarlier()
    {
        if (Earlier == 0)
        {
            return false;
        }

        Set(first - Step, first + count - 1, keepsEnd: false);
        return true;
    }

    /// <summary>Shows a step more of what comes after. False when nothing does.</summary>
    public bool ShowLater()
    {
        if (Later == 0)
        {
            return false;
        }

        Set(first, first + count - 1 + Step, keepsEnd: true);
        return true;
    }

    /// <summary>
    /// Brings the end of the list onto the page: joined to what is shown when it is near, on its own when it is far.
    /// False when it is there already.
    /// </summary>
    public bool ShowNewest()
    {
        if (Later == 0)
        {
            return false;
        }

        Set(Later <= Page ? first : items.Count - Page, items.Count - 1, keepsEnd: true);
        return true;
    }

    /// <summary>
    /// Brings one item onto the page: the window grows to it when it is near, and moves to stand around it when it is
    /// far, so landing anywhere in the list costs the same. False when it is shown already or not listed.
    /// </summary>
    public bool Reach(string wanted)
    {
        var at = Find(wanted, -1);
        var last = first + count - 1;

        if (at < 0 || (at >= first && at <= last))
        {
            return false;
        }

        if (at < first && first - at <= Page)
        {
            Set(at - Step, last, keepsEnd: false);
        }
        else if (at > last && at - last <= Page)
        {
            Set(first, at + Step, keepsEnd: true);
        }
        else
        {
            Set(at - (Page / 2), at + (Page / 2), keepsEnd: false);
        }

        return true;
    }

    #endregion

    #region Private Methods

    // The window from one place to another, widened to whole groups and held to the most the page keeps.
    private void Set(int start, int end, bool keepsEnd)
    {
        if (items.Count == 0)
        {
            (first, count, firstKey, lastKey, Shown) = (0, 0, null, null, items);
            return;
        }

        end = GroupEnd(Math.Clamp(end, 0, items.Count - 1));
        start = GroupStart(Math.Clamp(start, 0, end));

        if (end - start + 1 > Most && keepsEnd)
        {
            start = StartWithin(end - Most + 1, end);
        }
        else if (end - start + 1 > Most)
        {
            end = GroupEnd(start + Most - 1);
        }

        (first, count) = (start, end - start + 1);
        (firstKey, lastKey) = (key(items[start]), key(items[end]));
        Shown = count == items.Count ? items : new Slice(items, first, count);
    }

    private bool Opens(int index) => opens?.Invoke(items[index]) ?? true;

    private int GroupStart(int index)
    {
        while (index > 0 && !Opens(index))
        {
            index--;
        }

        return index;
    }

    private int GroupEnd(int index)
    {
        while (index < items.Count - 1 && !Opens(index + 1))
        {
            index++;
        }

        return index;
    }

    // The first group that begins at or after a place; a single group longer than the page keeps is shown whole.
    private int StartWithin(int index, int end)
    {
        for (var at = index; at <= end; at++)
        {
            if (Opens(at))
            {
                return at;
            }
        }

        return GroupStart(index);
    }

    // Where a key is now. It is most often where it was, or near the end of the list.
    private int Find(string? wanted, int hint)
    {
        if (wanted is null)
        {
            return -1;
        }

        if (hint >= 0 && hint < items.Count && key(items[hint]) == wanted)
        {
            return hint;
        }

        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (key(items[index]) == wanted)
            {
                return index;
            }
        }

        return -1;
    }

    #endregion

    #region Nested Types

    private sealed class Slice(IReadOnlyList<T> all, int start, int count) : IReadOnlyList<T>
    {
        #region Properties

        public int Count => count;

        public T this[int index] =>
            index >= 0 && index < count ? all[start + index] : throw new ArgumentOutOfRangeException(nameof(index));

        #endregion

        #region Public Methods

        public IEnumerator<T> GetEnumerator()
        {
            for (var index = 0; index < count; index++)
            {
                yield return all[start + index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }

    #endregion
}
