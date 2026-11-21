namespace Drover.Native.Chat;

/// <summary>
/// Gathers the changes made to a piece in one go into one redraw. A conversation sets several values on a card at
/// once, and a view model changes several things in one step; the piece hands its values to its view model once, and
/// draws once, after all of them are in.
/// </summary>
internal sealed class Batch(FrameworkElement owner, Action apply, Action draw)
{
    #region Private Fields

    private bool queued;

    private bool valuesChanged = true;

    #endregion

    #region Public Methods

    /// <summary>A value the piece was given changed: hand the values over again, then draw.</summary>
    public void Values()
    {
        valuesChanged = true;
        Queue();
    }

    /// <summary>Something the piece shows changed: draw.</summary>
    public void Draw() => Queue();

    /// <summary>
    /// Draws now if a draw is waiting, so a piece measured for the first time already shows what it was given, not an
    /// empty frame until the queued draw comes round.
    /// </summary>
    public void DrawIfWaiting()
    {
        if (queued)
        {
            Run();
        }
    }

    /// <summary>Does now what is waiting, for a piece that must be drawn before it is measured.</summary>
    public void Flush()
    {
        if (valuesChanged)
        {
            valuesChanged = false;
            apply();
        }

        draw();
    }

    #endregion

    #region Private Methods

    private void Queue()
    {
        if (queued)
        {
            return;
        }

        queued = true;
        if (!owner.DispatcherQueue.TryEnqueue(Run))
        {
            Run();
        }
    }

    // A draw that already ran before it was measured leaves nothing for the queued one to do.
    private void Run()
    {
        if (!queued)
        {
            return;
        }

        queued = false;
        Flush();
    }

    #endregion
}
