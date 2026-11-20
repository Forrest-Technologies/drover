namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// Saves a conversation as a file: the server writes it, as Markdown or as JSON, and the device saves it where the
/// person chooses. The device's way of saving is handed in, as the extension editor's download is. How it ended is
/// said in a toast; a save the person cancelled says nothing.
/// </summary>
public sealed class ConversationSaver(
    DroverApi api,
    SessionState session,
    ToastService toasts,
    ILogger<ConversationSaver> logger)
{
    #region Private Fields

    private bool saving;

    #endregion

    #region Public Methods

    /// <summary>
    /// Asks the server for the conversation in <paramref name="format"/> and hands it to <paramref name="save"/>,
    /// which takes a file name, the text, and its media type, and says whether the file was saved. A second press
    /// while one save is under way does nothing.
    /// </summary>
    public async Task Save(Guid? threadId, string format, Func<string, string, string, Task<bool>> save)
    {
        if (saving)
        {
            return;
        }

        if (threadId is not { } thread || session.Space is not { } space)
        {
            toasts.Show("Nothing has been said in this conversation yet, so there is nothing to save.", ToastKind.Info);
            return;
        }

        saving = true;
        try
        {
            var file = await api.ExportConversation(space.Id, thread, format);
            if (await save(file.Name, file.Text, file.MediaType))
            {
                toasts.Show($"Saved {file.Name}.", ToastKind.Success);
            }
        }
        catch (DeviceCallFailedException exception)
        {
            logger.LogWarning(exception, "A conversation could not be handed to the device to save");
            toasts.Show("This device did not save the conversation. Try again, or choose another place to save it.", ToastKind.Error);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationSaver));
            toasts.Show($"The conversation could not be saved. {Failures.Sentence(exception)}", ToastKind.Error);
        }
        finally
        {
            saving = false;
        }
    }

    #endregion

    #region Fixed Words

    public const string MarkdownLabel = "Save conversation as Markdown";

    public const string JsonLabel = "Save conversation as JSON";

    #endregion
}
