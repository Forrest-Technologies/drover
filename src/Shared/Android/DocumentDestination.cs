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
                throw new FileDeliveryException("Another file is being saved, or the app is not ready. Try again in a moment.");
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
                throw new FileDeliveryException("This device has no file picker.", missing);
            }
            finally
            {
                waiting = null;
            }
        });

    internal void Complete(int request, Result outcome, Intent? data)
    {
        if (request != CreateDocument)
        {
            return;
        }

        if (waiting is { } result)
        {
            result.TrySetResult(outcome == Result.Ok ? data?.Data : null);
        }
        else if (outcome == Result.Ok && data?.Data is { } orphan)
        {
            // Android stopped the app while its picker was open, so nothing is waiting to fill the document the picker
            // made. Left alone it would stay empty where the person put it, and nothing would say so.
            Remove(orphan);
            Tell("A save was interrupted, so no file was saved. Save it again.");
        }
    }

    /// <summary>Deletes a document a save began and did not finish. A document already gone is not an error.</summary>
    internal static void Remove(AndroidUri document, ILogger? logger = null)
    {
        try
        {
            global::Android.Provider.DocumentsContract.DeleteDocument(AndroidApplication.Context.ContentResolver!, document);
        }
        catch (Exception exception) when (exception is Java.Lang.Exception or IOException)
        {
            logger?.LogWarning(exception, "A document left by a failed save could not be removed");
        }
    }

    /// <summary>Says a sentence to the person, on the screen, from any thread.</summary>
    internal static void Tell(string words) =>
        MainThread.BeginInvokeOnMainThread(() =>
            global::Android.Widget.Toast.MakeText(AndroidApplication.Context, words, global::Android.Widget.ToastLength.Long)?.Show());

    #endregion
}
