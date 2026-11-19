namespace Drover.Ui;

/// <summary>Which way an arrow key, Home, or End moves through a list of items.</summary>
public enum ArrowMotionKey
{
    /// <summary>Down or Right: the next item.</summary>
    Next,

    /// <summary>Up or Left: the previous item.</summary>
    Previous,

    /// <summary>Home: the first item.</summary>
    First,

    /// <summary>End: the last item.</summary>
    Last,
}

/// <summary>
/// Which item the arrow keys, Home, and End move to. Down and Right move to the next item, Up and Left to the
/// previous, and the ends stay on the first and last item.
/// </summary>
public static class ArrowMotion
{
    /// <summary>The index to move to, or nothing when the list is empty.</summary>
    public static int? Next(int current, int count, ArrowMotionKey key)
    {
        if (count <= 0)
        {
            return null;
        }

        return key switch
        {
            ArrowMotionKey.Next => Math.Min(current + 1, count - 1),
            ArrowMotionKey.Previous => Math.Max(current - 1, 0),
            ArrowMotionKey.First => 0,
            ArrowMotionKey.Last => count - 1,
            _ => null,
        };
    }
}
