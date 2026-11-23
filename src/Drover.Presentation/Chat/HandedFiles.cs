namespace Drover.Ui.Chat;

/// <summary>
/// What one choice, paste or drop handed to a conversation: the files to take, how many folders it left out (a
/// message carries files), and how many files came past the most one go takes. Those were counted, not read.
/// </summary>
public sealed record HandedFiles(IReadOnlyList<IPickedFile> Files, int Folders = 0, int Beyond = 0)
{
    #region Public Methods

    /// <summary>
    /// Reads the first of the items handed over, as many as one go takes, and counts the rest without reading them:
    /// reading an item past the limit, which may be slow or fail, could only hold up the ones that are taken.
    /// </summary>
    public static async Task<HandedFiles> ReadFirst<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, Task<IPickedFile>> read,
        int folders = 0)
    {
        List<IPickedFile> files = [];

        foreach (var item in items.Take(ComposerViewModel.MaxFilesAtOnce))
        {
            files.Add(await read(item));
        }

        return new(files, folders, items.Count - files.Count);
    }

    #endregion
}
