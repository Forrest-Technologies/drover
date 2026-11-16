namespace Drover.Engine.Files;

/// <summary>
/// What one run's model is given for the files people attached to its conversation. Rows are read as the run's
/// person, so a file they cannot see is never given. A picture goes to a model that reads pictures; text goes in as
/// text, up to <see cref="MaxTextCharacters"/>; anything else is copied into the bot's sandbox, once a run, and the
/// model is told where, but only from a conversation everyone who can reach the sandbox can see, checked when the rows
/// are read (<see cref="FileAbilityReader.AudienceCoversSandbox"/>). Nothing here holds a
/// transaction while it reads a file or copies one. A file is text from outside Drover, so a run given one, its
/// content or its copy in the sandbox, is tainted (<paramref name="taint"/>).
/// </summary>
public sealed class RunFiles(
    RunTenant tenant,
    Guid botId,
    bool modelReadsImages,
    bool hasSandbox,
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

    private readonly ConcurrentDictionary<Guid, FileForModel> given = new();

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
            opened[file.Id] = await Give(file, new FileAbilities(modelReadsImages, hasSandbox, covered), cancellationToken);
        }

        return opened;
    }

    /// <summary>Where an attached file is put in the bot's sandbox, relative to its home folder.</summary>
    public static string SandboxPath(StoredFile file) => $"{SandboxFolder}/{file.Id:N}/{file.Name}";

    #endregion

    #region Private Methods

    /// <summary>A picture is read each time it is given, so its bytes are not held for the whole run.</summary>
    private async Task<FileForModel> Give(StoredFile file, FileAbilities can, CancellationToken cancellationToken)
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
            FileUse.Sandbox => await IntoSandbox(file, cancellationToken),
            _ => Note(file, FileUses.RefusalFor(kind, can) switch
            {
                FileRefusals.PrivateConversation => "this conversation is private and others in this space can reach your sandbox, so it was not put there and you cannot open it",
                FileRefusals.ModelCannotReadImages => "this bot's model cannot read pictures and it has no sandbox, so it cannot open it",
                _ => "this bot has no sandbox to open it in, so it cannot open it",
            }),
        };

        if (use is not FileUse.Image)
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
        return new FileForModel(file.Id, $"[The person attached a picture, {file.Name}, {file.Width} by {file.Height} pixels:]", copy.ToArray(), file.ContentType);
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
            await sandboxes.PutFile(new SandboxCaller(tenant, botId), path, content, file.Size, cancellationToken);
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

    #endregion
}
