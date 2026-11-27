namespace Drover.Presentation.Tests.Support;

/// <summary>
/// A device's listener for a model sign-in's return, played in memory: a test says whether the device can listen,
/// whether it can now, and brings the browser's return when it likes.
/// </summary>
public sealed class FakeModelSignInListener : IModelSignInListener
{
    #region Properties

    public static Uri RedirectUri { get; } = new("http://127.0.0.1:1455/auth/callback");

    public bool CanListen { get; set; } = true;

    /// <summary>When true, the next <see cref="Open"/> fails, as when no port is free.</summary>
    public bool Fails { get; set; }

    public FakeReturn? Current { get; private set; }

    public int Opened { get; private set; }

    /// <summary>The return address the last <see cref="Open"/> was asked to listen at.</summary>
    public Uri? Wanted { get; private set; }

    #endregion

    #region Interface Implementations

    public IModelSignInReturn Open(Uri? wanted = null)
    {
        if (Fails)
        {
            throw new DeviceCallFailedException("No port is free.");
        }

        Opened++;
        Wanted = wanted;
        Current = new FakeReturn();
        return Current;
    }

    #endregion

    #region Nested Types

    /// <summary>One return being waited for.</summary>
    public sealed class FakeReturn : IModelSignInReturn
    {
        #region Private Fields

        private readonly TaskCompletionSource<string> returned = new(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Properties

        public Uri RedirectUri => FakeModelSignInListener.RedirectUri;

        public Task<string> Returned => returned.Task;

        public bool Disposed { get; private set; }

        #endregion

        #region Public Methods

        /// <summary>What the browser brings back after the person approved.</summary>
        public void Bring(string query) => returned.TrySetResult(query);

        public void Dispose() => Disposed = true;

        #endregion
    }

    #endregion
}
