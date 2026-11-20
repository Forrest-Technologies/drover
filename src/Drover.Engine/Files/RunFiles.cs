namespace Drover.Engine.Files;

/// <summary>
/// What one run's model is given for the files people attached to its conversation or shared in its project. Rows
/// are read as the run's person, so a file they cannot see is never given. A picture goes to a model that reads
/// pictures; text goes in as text, up to <see cref="MaxTextCharacters"/>; a document's text is read out on the server
/// (<see cref="DocumentReader"/>) and goes in the same way, from at most <paramref name="maxDocumentPages"/> of its
/// pages, and a PDF with no text goes to a model that reads pictures as the pictures of its first pages; anything else
/// from a conversation attachment is copied into the bot's sandbox, once a run, and the model is told where, as a
/// document is too when the bot has a sandbox it may use, but only from a conversation everyone who can reach the
/// sandbox can see, checked when the rows are read (<see cref="FileAbilityReader.AudienceCoversSandbox"/>) and again
/// when the copy is written. Shared project files never copy into a sandbox and reach the model directly as text,
/// read documents, or pictures. Nothing here holds a transaction while it reads a file or copies one. A file is text
/// from outside Drover, so a run given one, its content or its copy in the sandbox, is tainted (<paramref name="taint"/>).
/// </summary>
public sealed class RunFiles(
    RunTenant tenant,
    Guid botId,
    bool modelReadsImages,
    bool hasSandbox,
    int maxDocumentPages,
    IFileStore store,
    SandboxBroker? sandboxes,
    Func<Task> taint,
    ILogger logger)
{
    #region Private Fields

    /// <summary>The most characters of one text file a model is given, as many as a person may type in one message.</summary>
    public const int MaxTextCharacters = RunService.MaxMessageLength;

    /// <summary>Where in the sandbox's home folder attached files are put, each in a folder named by its id.</summary>
    public const string SandboxFolder = "attachments";

    /// <summary>The most pages of a PDF with no text a model is shown as pictures.</summary>
    public const int MaxPageImages = 20;

    private readonly ConcurrentDictionary<Guid, FileForModel> given = new();

    /// <summary>Project pictures retained only within this run after their bounded first read.</summary>
    private readonly ConcurrentDictionary<Guid, FileForModel> projectPictures = new();

    #endregion

    #region Public Methods

    public async Task<IReadOnlyDictionary<Guid, FileForModel>> Open(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken)
    {
        Dictionary<Guid, FileForModel> opened = [];

        if (fileIds.Count == 0)
        {
            return opened;
        }

        // Each row's audience is its conversation's as it is now, and the space's members are read in the same
        // transaction, so a conversation made private, or a personal space that gained a member, takes no copy.
        var rows = await tenant.Transact(
            async services =>
            {
                var files = await services.GetRequiredService<FileRepository>().ListFiles(fileIds, cancellationToken);
                var accounts = services.GetRequiredService<AccountRepository>();
                List<(StoredFile File, bool Covered)> read = [];

                foreach (var file in files)
                {
                    read.Add((file, await FileAbilityReader.AudienceCoversSandbox(tenant.SpaceId, file.AudienceUserId, accounts, cancellationToken)));
                }

                return read;
            },
            cancellationToken);

        foreach (var (file, covered) in rows)
        {
            opened[file.Id] = await Give(file, new FileAbilities(modelReadsImages, hasSandbox, covered), forProject: false, cancellationToken);
        }

        return opened;
    }

    /// <summary>
    /// Opens shared files belonging to <paramref name="projectId"/> newest-first without touching any sandbox,
    /// enforcing per-call character, image count, encoded byte, decoded pixel byte, and dimension bounds as each file
    /// is read so discarded outputs are never retained.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, FileForModel>> OpenProject(
        Guid projectId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, FileForModel> opened = [];

        if (fileIds.Count == 0)
        {
            return opened;
        }

        Dictionary<Guid, int> rank = [];
        var order = 0;

        foreach (var id in fileIds)
        {
            rank[id] = order++;
        }

        var rows = await tenant.Transact(
            async services =>
            {
                var files = await services.GetRequiredService<FileRepository>().ListFiles(fileIds, cancellationToken);
                return files
                    .Where(file => file.ProjectId == projectId)
                    .OrderByDescending(file => rank.GetValueOrDefault(file.Id, -1))
                    .ToList();
            },
            cancellationToken);
        FileAbilities directOnly = new(modelReadsImages, HasSandbox: false, AudienceCoversSandbox: false);
        var budget = new ProjectCallBudget();
        var anyKept = false;

        foreach (var file in rows)
        {
            if (projectPictures.TryGetValue(file.Id, out var cachedPicture))
            {
                if (!TryMeasureImages(cachedPicture, out var cachedImages, out var cachedBytes, out var cachedDecoded)
                    || !budget.CanFitImages(cachedImages, cachedBytes, cachedDecoded)
                    || (anyKept && cachedPicture.Text.Length + 1 > budget.RemainingCharacters))
                {
                    break;
                }

                await taint();
                opened[file.Id] = cachedPicture;
                budget.Consume(cachedPicture.Text.Length + 1, cachedImages, cachedBytes, cachedDecoded);
                anyKept = true;
                continue;
            }

            if (given.TryGetValue(file.Id, out var known))
            {
                if (anyKept && known.Text.Length + 1 > budget.RemainingCharacters)
                {
                    break;
                }

                await taint();
                opened[file.Id] = known;
                budget.ConsumeText(known.Text.Length + 1);
                anyKept = true;
                continue;
            }

            var kind = FileInspector.KindOf(file.ContentType);
            var use = FileUses.For(kind, directOnly);

            if (use is FileUse.None)
            {
                var refused = Note(file, FileUses.RefusalFor(kind, directOnly) is FileRefusals.ModelCannotReadImages
                    ? "this bot's model cannot read pictures and project files are not copied into a sandbox, so it cannot open it"
                    : "project files are not copied into a sandbox, so it cannot open it");

                if (anyKept && refused.Text.Length + 1 > budget.RemainingCharacters)
                {
                    break;
                }

                opened[file.Id] = refused;
                budget.ConsumeText(refused.Text.Length + 1);
                anyKept = true;
                continue;
            }

            if (anyKept && budget.RemainingCharacters <= 0)
            {
                break;
            }

            if (use is FileUse.Text)
            {
                await taint();
                var textResult = await Text(file, cancellationToken);

                if (anyKept && textResult.Text.Length + 1 > budget.RemainingCharacters)
                {
                    break;
                }

                opened[file.Id] = textResult;
                given[file.Id] = textResult;
                budget.ConsumeText(textResult.Text.Length + 1);
                anyKept = true;
                continue;
            }

            if (use is FileUse.Image)
            {
                if (file.Width is > 0 and var knownWidth && file.Height is > 0 and var knownHeight)
                {
                    if (!FitsSingleImage(knownWidth, knownHeight, file.Size))
                    {
                        continue;
                    }

                    if (!budget.CanFitImage(file.Size, (long)knownWidth * knownHeight * 4L))
                    {
                        break;
                    }
                }
                else if (file.Size > ProjectFileLimits.MaxImageBytes)
                {
                    continue;
                }

                var picture = await Picture(file, cancellationToken);

                if (picture.Images is [{ } image])
                {
                    var hasSize = FileInspector.TryReadImageDimensions(image.Data.Span, out var width, out var height);

                    if (!hasSize && file.Width is > 0 and var storedW && file.Height is > 0 and var storedH)
                    {
                        width = storedW;
                        height = storedH;
                        hasSize = true;
                    }

                    if (!hasSize)
                    {
                        continue;
                    }

                    if (!FitsSingleImage(width, height, image.Data.Length))
                    {
                        continue;
                    }

                    var decoded = (long)width * height * 4L;

                    if (!budget.CanFitImage(image.Data.Length, decoded))
                    {
                        break;
                    }

                    if (anyKept && picture.Text.Length + 1 > budget.RemainingCharacters)
                    {
                        break;
                    }

                    await taint();
                    opened[file.Id] = picture;
                    projectPictures[file.Id] = picture;
                    budget.Consume(picture.Text.Length + 1, 1, image.Data.Length, decoded);
                    anyKept = true;
                }
                else
                {
                    if (anyKept && picture.Text.Length + 1 > budget.RemainingCharacters)
                    {
                        break;
                    }

                    await taint();
                    opened[file.Id] = picture;
                    budget.ConsumeText(picture.Text.Length + 1);
                    anyKept = true;
                }

                continue;
            }

            if (use is FileUse.Document)
            {
                if (file.Size < 0 || file.Size > ProjectFileLimits.MaxStoredBytes)
                {
                    continue;
                }

                if (await ReadAll(file, cancellationToken) is not { } bytes)
                {
                    await taint();
                    var missing = Missing(file);

                    if (anyKept && missing.Text.Length + 1 > budget.RemainingCharacters)
                    {
                        break;
                    }

                    opened[file.Id] = missing;
                    budget.ConsumeText(missing.Text.Length + 1);
                    anyKept = true;
                    continue;
                }

                var imageCap = modelReadsImages ? Math.Min(MaxPageImages, budget.RemainingImages) : 0;
                DocumentLimits limits = new(
                    maxDocumentPages,
                    MaxTextCharacters,
                    imageCap,
                    budget.RemainingImageBytes,
                    budget.RemainingDecodedImageBytes,
                    ProjectFileLimits.MaxImageDimension,
                    ProjectFileLimits.MaxImagePixels,
                    ProjectFileLimits.MaxImageBytes,
                    ProjectFileLimits.MaxDecodedImageBytes);
                var (read, unreadable) = ReadDocument(file, bytes, limits);

                if (read is null)
                {
                    await taint();
                    var note = Note(file, $"it could not be read: {unreadable}");

                    if (anyKept && note.Text.Length + 1 > budget.RemainingCharacters)
                    {
                        break;
                    }

                    opened[file.Id] = note;
                    budget.ConsumeText(note.Text.Length + 1);
                    anyKept = true;
                    continue;
                }

                if (!read.HasText && modelReadsImages)
                {
                    if (budget.RemainingImages <= 0)
                    {
                        break;
                    }

                    if (read.ImagesRejected)
                    {
                        if (read.ImagesExceedSingleLimits || !anyKept)
                        {
                            continue;
                        }

                        break;
                    }

                    if (budget.RemainingImages < MaxPageImages
                        && read.PageImages.Count == budget.RemainingImages
                        && read.Total > budget.RemainingImages)
                    {
                        break;
                    }

                    var docImages = new FileForModel(file.Id, DocumentWords.WithoutText(file.Name, file.ContentType, read), read.PageImages);

                    if (anyKept && docImages.Text.Length + 1 > budget.RemainingCharacters)
                    {
                        break;
                    }

                    var encodedBytes = read.PageImages.Sum(image => (long)image.Data.Length);
                    await taint();
                    opened[file.Id] = docImages;
                    projectPictures[file.Id] = docImages;
                    budget.Consume(docImages.Text.Length + 1, read.PageImages.Count, encodedBytes, read.PageImageDecodedBytes);
                    anyKept = true;
                    continue;
                }

                var docText = read.HasText
                    ? new FileForModel(file.Id, DocumentWords.ForModel(file.Name, file.ContentType, read, MaxTextCharacters))
                    : new FileForModel(file.Id, DocumentWords.WithoutText(file.Name, file.ContentType, read));

                if (anyKept && docText.Text.Length + 1 > budget.RemainingCharacters)
                {
                    break;
                }

                await taint();
                opened[file.Id] = docText;
                given[file.Id] = docText;
                budget.ConsumeText(docText.Text.Length + 1);
                anyKept = true;
            }
        }

        return opened;
    }

    /// <summary>Where an attached file is put in the bot's sandbox, relative to its home folder.</summary>
    public static string SandboxPath(StoredFile file) => $"{SandboxFolder}/{file.Id:N}/{file.Name}";

    #endregion

    #region Private Methods

    /// <summary>A picture is read each time it is given, so its bytes are not held for the whole run. So are pictures of pages.</summary>
    private async Task<FileForModel> Give(StoredFile file, FileAbilities can, bool forProject, CancellationToken cancellationToken)
    {
        if (given.TryGetValue(file.Id, out var known))
        {
            return known;
        }

        var kind = FileInspector.KindOf(file.ContentType);
        var use = FileUses.For(kind, can);

        if (use is not FileUse.None)
        {
            await taint();
        }

        var result = use switch
        {
            FileUse.Image => await Picture(file, cancellationToken),
            FileUse.Text => await Text(file, cancellationToken),
            FileUse.Document => await Document(file, can, cancellationToken),
            FileUse.Sandbox => await IntoSandbox(file, cancellationToken),
            _ => Note(file, (forProject, FileUses.RefusalFor(kind, can)) switch
            {
                (true, FileRefusals.ModelCannotReadImages) => "this bot's model cannot read pictures and project files are not copied into a sandbox, so it cannot open it",
                (true, _) => "project files are not copied into a sandbox, so it cannot open it",
                (false, FileRefusals.PrivateConversation) => "this conversation is private and others in this space can reach your sandbox, so it was not put there and you cannot open it",
                (false, FileRefusals.ModelCannotReadImages) => "this bot's model cannot read pictures and it has no sandbox, so it cannot open it",
                (false, _) => "this bot has no sandbox to open it in, so it cannot open it",
            }),
        };

        if (result.Images is not { Count: > 0 })
        {
            given[file.Id] = result;
        }

        return result;
    }

    private async Task<FileForModel> Picture(StoredFile file, CancellationToken cancellationToken)
    {
        await using var content = await store.Open(tenant.SpaceId, file.StorageKey, cancellationToken);

        if (content is null)
        {
            return Missing(file);
        }

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        return new FileForModel(
            file.Id,
            $"[The person attached a picture, {file.Name}, {file.Width} by {file.Height} pixels:]",
            [new ToolImage(copy.ToArray(), file.ContentType)]);
    }

    /// <summary>
    /// The text read out of a document, or for a PDF with no text, pictures of its pages. With a sandbox the bot may
    /// use, the file is put there as well, so its tools can work on the original.
    /// </summary>
    private async Task<FileForModel> Document(StoredFile file, FileAbilities can, CancellationToken cancellationToken)
    {
        if (await ReadAll(file, cancellationToken) is not { } bytes)
        {
            return Missing(file);
        }

        var (read, unreadable) = ReadDocument(file, bytes, can.ModelReadsImages);
        var given = read switch
        {
            null => Note(file, $"it could not be read: {unreadable}"),
            { HasText: true } => new FileForModel(file.Id, DocumentWords.ForModel(file.Name, file.ContentType, read, MaxTextCharacters)),
            _ => new FileForModel(file.Id, DocumentWords.WithoutText(file.Name, file.ContentType, read), read.PageImages),
        };

        if (can is not { HasSandbox: true, AudienceCoversSandbox: true })
        {
            return given;
        }

        var placed = await IntoSandbox(file, cancellationToken);
        return given with { Text = $"{given.Text}\n{placed.Text}" };
    }

    private async Task<FileForModel> Text(StoredFile file, CancellationToken cancellationToken)
    {
        await using var content = await store.Open(tenant.SpaceId, file.StorageKey, cancellationToken);

        if (content is null)
        {
            return Missing(file);
        }

        using var reader = new StreamReader(content, Encoding.UTF8);
        var buffer = new char[MaxTextCharacters];
        var read = await reader.ReadBlockAsync(buffer, cancellationToken);
        var cut = reader.Peek() >= 0;
        var text = new string(buffer, 0, read);
        var ending = cut
            ? $"[{file.Name} continues. Only its first {MaxTextCharacters} characters are shown.]"
            : $"[End of {file.Name}.]";
        return new FileForModel(file.Id, $"[The person attached {file.Name}. Its content follows.]\n{text}\n{ending}");
    }

    private (DocumentContent? Read, string? Unreadable) ReadDocument(StoredFile file, byte[] bytes, bool modelReadsImages) =>
        ReadDocument(file, bytes, new DocumentLimits(maxDocumentPages, MaxTextCharacters, modelReadsImages ? MaxPageImages : 0));

    private (DocumentContent? Read, string? Unreadable) ReadDocument(StoredFile file, byte[] bytes, DocumentLimits limits)
    {
        try
        {
            return (DocumentReader.Read(bytes, file.ContentType, limits), null);
        }
        catch (DocumentUnreadableException unreadable)
        {
            // The parser's own message may quote the file, so only the kind of failure is logged.
            logger.LogWarning("Document {FileId} could not be read: {Failure}", file.Id, unreadable.InnerException?.GetType().Name);
            return (null, unreadable.Message);
        }
    }

    private async Task<byte[]?> ReadAll(StoredFile file, CancellationToken cancellationToken)
    {
        await using var content = await store.Open(tenant.SpaceId, file.StorageKey, cancellationToken);

        if (content is null)
        {
            return null;
        }

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }

    private async Task<FileForModel> IntoSandbox(StoredFile file, CancellationToken cancellationToken)
    {
        if (sandboxes is null)
        {
            return Note(file, "this server has no sandboxes, so it cannot be opened");
        }

        await using var content = await store.Open(tenant.SpaceId, file.StorageKey, cancellationToken);

        if (content is null)
        {
            return Missing(file);
        }

        var path = SandboxPath(file);

        try
        {
            await sandboxes.PutFile(new SandboxCaller(tenant, botId), path, content, file.Size, file.Id, cancellationToken);
        }
        catch (BuiltinToolException refused)
        {
            logger.LogWarning("File {FileId} could not be put in the sandbox of bot {BotId}", file.Id, botId);
            return Note(file, $"it could not be put in the sandbox: {refused.Message}");
        }

        logger.LogInformation("File {FileId} was put in the sandbox of bot {BotId}", file.Id, botId);
        return new FileForModel(file.Id, $"[The person attached {file.Name} ({file.ContentType}, {FileWords.Size(file.Size)}). It is in your sandbox at {path}, in the home folder.]");
    }

    private FileForModel Missing(StoredFile file)
    {
        logger.LogWarning("File {FileId} has a row and nothing in the file store", file.Id);
        return Note(file, "it could not be found on the server");
    }

    private static FileForModel Note(StoredFile file, string why) =>
        new(file.Id, $"[The person attached {file.Name} ({file.ContentType}, {FileWords.Size(file.Size)}), but {why}.]");

    private static bool FitsSingleImage(int width, int height, long encodedBytes)
    {
        if (width <= 0 || height <= 0
            || width > ProjectFileLimits.MaxImageDimension
            || height > ProjectFileLimits.MaxImageDimension)
        {
            return false;
        }

        var pixels = (long)width * height;
        var decoded = pixels * 4L;
        return pixels <= ProjectFileLimits.MaxImagePixels
            && decoded <= ProjectFileLimits.MaxDecodedImageBytes
            && encodedBytes <= ProjectFileLimits.MaxImageBytes;
    }

    private static bool TryMeasureImages(
        FileForModel file,
        out int count,
        out long encodedBytes,
        out long decodedBytes)
    {
        count = 0;
        encodedBytes = 0;
        decodedBytes = 0;

        foreach (var image in file.Images ?? [])
        {
            if (!FileInspector.TryReadImageDimensions(image.Data.Span, out var width, out var height)
                || !FitsSingleImage(width, height, image.Data.Length))
            {
                return false;
            }

            count++;
            encodedBytes += image.Data.Length;
            decodedBytes += (long)width * height * 4L;
        }

        return count <= ProjectFileLimits.MaxImages
            && encodedBytes <= ProjectFileLimits.MaxImageBytes
            && decodedBytes <= ProjectFileLimits.MaxDecodedImageBytes;
    }

    #endregion

    #region Nested Types

    private sealed class ProjectCallBudget
    {
        #region Properties

        public int RemainingCharacters { get; private set; } = ProjectFileLimits.MaxCharacters;

        public int RemainingImages { get; private set; } = ProjectFileLimits.MaxImages;

        public long RemainingImageBytes { get; private set; } = ProjectFileLimits.MaxImageBytes;

        public long RemainingDecodedImageBytes { get; private set; } = ProjectFileLimits.MaxDecodedImageBytes;

        #endregion

        #region Public Methods

        public bool CanFitImage(long encodedBytes, long decodedBytes) =>
            CanFitImages(1, encodedBytes, decodedBytes);

        public bool CanFitImages(int images, long encodedBytes, long decodedBytes) =>
            images <= RemainingImages
            && encodedBytes <= RemainingImageBytes
            && decodedBytes <= RemainingDecodedImageBytes;

        public void ConsumeText(int characters) =>
            RemainingCharacters = Math.Max(0, RemainingCharacters - Math.Max(0, characters));

        public void Consume(int characters, int images, long encodedBytes, long decodedBytes)
        {
            ConsumeText(characters);
            RemainingImages = Math.Max(0, RemainingImages - Math.Max(0, images));
            RemainingImageBytes = Math.Max(0, RemainingImageBytes - Math.Max(0, encodedBytes));
            RemainingDecodedImageBytes = Math.Max(0, RemainingDecodedImageBytes - Math.Max(0, decodedBytes));
        }

        #endregion
    }

    #endregion
}
