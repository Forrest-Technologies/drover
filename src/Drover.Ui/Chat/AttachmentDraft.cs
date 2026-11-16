namespace Drover.Ui.Chat;

/// <summary>
/// The files chosen for the message being written, and where each stands. It holds no connection: the conversation
/// uploads each file and tells it what came of that. A file past the server's limits is refused here, before any of
/// it is sent; the server decides the rest from the file's bytes when it arrives.
/// </summary>
public sealed class AttachmentDraft
{
    #region Private Fields

    private readonly List<Entry> entries = [];

    #endregion

    #region Properties

    public IReadOnlyList<AttachmentItem> Items => [.. entries.Select(entry => entry.Item)];

    public bool IsEmpty => entries.Count == 0;

    /// <summary>Every file is uploaded and none is refused. True when there are none.</summary>
    public bool CanSend => FileWords.CanSend(Items);

    /// <summary>The server's ids of the uploaded files, in the order they were chosen.</summary>
    public IReadOnlyList<Guid> FileIds => [.. entries.Select(entry => entry.FileId).OfType<Guid>()];

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds a chosen file. It is uploading unless a limit refuses it: larger than the server takes, or one more
    /// than a message may carry. Without the limits (they could not be read) the server decides alone.
    /// </summary>
    public AttachmentItem Add(string name, long size, AttachmentRules? rules)
    {
        // Only files that can still go count: a refused one or one that failed holds no place on the message.
        var counted = entries.Count(entry =>
            entry.Item is { Refusal: FileRefusal.None, Phase: not UploadPhase.Failed });
        var refusal = rules switch
        {
            { MaxFileBytes: var largest } when size > largest => FileRefusal.TooLarge,
            { MaxFilesPerMessage: var most } when counted >= most => FileRefusal.TooMany,
            _ => FileRefusal.None,
        };
        var item = new AttachmentItem(
            Guid.NewGuid(),
            name,
            size,
            refusal is FileRefusal.None ? UploadPhase.Uploading : UploadPhase.Ready,
            0,
            refusal);
        entries.Add(new Entry(item, null));
        return item;
    }

    /// <summary>How much of the file has been sent. True when the percent said in words changed.</summary>
    public bool Progress(Guid id, long sent)
    {
        if (Find(id) is not { Item.Phase: UploadPhase.Uploading } entry)
        {
            return false;
        }

        var percent = entry.Item.Size > 0 ? (int)Math.Clamp(sent * 100 / entry.Item.Size, 0, 99) : 99;

        if (percent == entry.Item.Percent)
        {
            return false;
        }

        Replace(entry, entry with { Item = entry.Item with { Percent = percent } });
        return true;
    }

    /// <summary>
    /// The server stored the file. False when it was taken off meanwhile, so the stored one is not wanted.
    /// </summary>
    public bool Uploaded(Guid id, FileResponse file)
    {
        if (Find(id) is not { } entry)
        {
            return false;
        }

        var ready = entry.Item with { Phase = UploadPhase.Ready, Percent = 100, Size = file.Size };
        Replace(entry, new Entry(ready, file.Id));
        return true;
    }

    /// <summary>
    /// The server refused the file for a reason it named, or for none this screen knows, which reads as a failure.
    /// </summary>
    public void Refused(Guid id, FileRefusal refusal)
    {
        if (Find(id) is { } entry)
        {
            Replace(entry, entry with
            {
                Item = refusal is FileRefusal.None
                    ? entry.Item with { Phase = UploadPhase.Failed }
                    : entry.Item with { Phase = UploadPhase.Ready, Refusal = refusal },
            });
        }
    }

    /// <summary>The upload did not get through.</summary>
    public void Failed(Guid id) => Refused(id, FileRefusal.None);

    /// <summary>Takes a file off. Its server id, when it was stored, so the stored file can be removed too.</summary>
    public Guid? Remove(Guid id)
    {
        if (Find(id) is not { } entry)
        {
            return null;
        }

        entries.Remove(entry);
        return entry.FileId;
    }

    /// <summary>
    /// The message went with these files. A file attached while it was on its way stays for the next one.
    /// </summary>
    public void Sent(IReadOnlyCollection<Guid> fileIds) =>
        entries.RemoveAll(entry => entry.FileId is { } fileId && fileIds.Contains(fileId));

    /// <summary>Forgets every file, as when the conversation is left.</summary>
    public void Clear() => entries.Clear();

    #endregion

    #region Private Methods

    private Entry? Find(Guid id) => entries.FirstOrDefault(entry => entry.Item.Id == id);

    private void Replace(Entry entry, Entry next) => entries[entries.IndexOf(entry)] = next;

    #endregion

    #region Nested Types

    private sealed record Entry(AttachmentItem Item, Guid? FileId);

    #endregion
}
