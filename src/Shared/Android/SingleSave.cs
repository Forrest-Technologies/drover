namespace Drover.App.Platforms.Android;

/// <summary>
/// One save at a time. A save is the picker, the copy into the place chosen, and the clean-up that follows, and the
/// marker that lets the next start remove a half-written document is one record, so a second save that began while
/// the first was still working could replace it and leave the first with none. A save that finds another working is
/// refused, with a reason, and not queued: its person is looking at a file card, not at a wait.
/// </summary>
public sealed class SingleSave
{
    #region Private Fields

    private int working;

    #endregion

    #region Public Methods

    /// <summary>The save's turn, to be disposed when its copy and clean-up are done, or null when a save is working.</summary>
    public IDisposable? TryStart() => Interlocked.CompareExchange(ref working, 1, 0) == 0 ? new Turn(this) : null;

    #endregion

    #region Nested Types

    private sealed class Turn(SingleSave owner) : IDisposable
    {
        #region Private Fields

        private int released;

        #endregion

        #region Interface Implementations

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                Volatile.Write(ref owner.working, 0);
            }
        }

        #endregion
    }

    #endregion
}
