namespace Drover.App.Platforms.Android;

public sealed class DocumentDestination
{
    #region Private Fields

    private const int CreateDocument = 16901;

    private TaskCompletionSource<AndroidUri?>? waiting;

    #endregion

    #region Public Methods

    public async Task<AndroidUri?> Choose(string name, string contentType, CancellationToken cancellationToken) =>
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (waiting is not null || Platform.CurrentActivity is not { } activity)
            {
                throw new InvalidOperationException("The Android file picker is unavailable.");
            }

            var result = new TaskCompletionSource<AndroidUri?>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting = result;
            using var intent = new Intent(Intent.ActionCreateDocument)
                .AddCategory(Intent.CategoryOpenable)!
                .SetType(contentType)!
                .PutExtra(Intent.ExtraTitle, name);
            try
            {
                activity.StartActivityForResult(intent, CreateDocument);
                return await result.Task.WaitAsync(cancellationToken);
            }
            catch (ActivityNotFoundException missing)
            {
                throw new IOException("This device has no file picker.", missing);
            }
            finally
            {
                waiting = null;
            }
        });

    internal void Complete(int request, Result outcome, Intent? data)
    {
        if (request == CreateDocument)
        {
            waiting?.TrySetResult(outcome == Result.Ok ? data?.Data : null);
        }
    }

    #endregion
}
