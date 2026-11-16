namespace Drover.Engine.Files;

/// <summary>
/// Files a bot shares from its sandbox into its conversation. The file is read from the one path the bot names, never
/// by looking through the sandbox, and kept through the same file store, keys, and limits as a file a person attaches.
/// The sandbox is shared, so the file may hold what another conversation put there; it belongs to the conversation
/// that asked for it, with that conversation's audience, and is added to the reply that asked for it. One call shares
/// one file, named by the call's id, so a call run again after a restart finds its file instead of keeping a second.
/// No transaction is open while bytes move.
/// </summary>
public sealed class FileSharing(
    SandboxBroker sandboxes,
    IFileStore store,
    IOptions<DeploymentPolicyOptions> deployment,
    TimeProvider time,
    ILogger<FileSharing> logger)
{
    #region Private Fields

    private const int MaxCaptionLength = 200;

    #endregion

    #region Properties

    private DeploymentPolicy Rules => deployment.Value.Rules;

    private long MaxSpaceBytes => Rules.MaxSpaceFileMegabytes * 1024L * 1024;

    #endregion

    #region Public Methods

    /// <summary>
    /// Shares the file at <paramref name="path"/> into the reply of the call. A file past a limit, a path with nothing
    /// at it, or a call outside a conversation is refused with <see cref="BuiltinToolException"/>, in words the bot can
    /// pass on.
    /// </summary>
    public async Task<StoredFile> Share(
        BuiltinToolCall call,
        string? environment,
        string path,
        string? caption,
        CancellationToken cancellationToken)
    {
        if (call.ThreadId is not { } threadId)
        {
            throw new BuiltinToolException("A file can be shared only into a conversation.");
        }

        var before = await Look(call, threadId, cancellationToken);

        if (before.Shared is { } shared)
        {
            return shared;
        }

        var spaceId = call.Tenant.SpaceId;
        var id = call.ToolCallId;
        var key = FileStorageKeys.For(spaceId, id);
        var file = await Copy(call, environment, path, key, before.Room, cancellationToken);

        try
        {
            var reply = await Keep(call, threadId, file, CleanCaption(caption, call.Redact), cancellationToken);
            await call.AnnounceMessage(reply);
        }
        catch (Exception)
        {
            await store.Delete(spaceId, key, CancellationToken.None);
            throw;
        }

        logger.LogInformation("Bot {BotId} shared file {FileId} of {Size} bytes into thread {ThreadId}", call.BotId, file.Id, file.Size, threadId);
        return file;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// The file this call already shared, if any; otherwise how many more bytes the space may hold, and a refusal
    /// when the reply carries as many files as a message may.
    /// </summary>
    private async Task<Before> Look(BuiltinToolCall call, Guid threadId, CancellationToken cancellationToken) =>
        await call.Tenant.Transact(
            async services =>
            {
                var files = services.GetRequiredService<FileRepository>();

                if (await files.FindFile(call.ToolCallId, cancellationToken) is { } shared)
                {
                    return new Before(shared, 0);
                }

                var reply = await services.GetRequiredService<MessageRepository>().FindReplyWithCall(threadId, call.RunId, call.ToolCallId, cancellationToken)
                    ?? throw new BuiltinToolException("The reply this file belongs to is not in the conversation, so it was not shared.");

                if (reply.Parts.Count(part => part is FilePart or ImagePart) >= Rules.MaxFilesPerMessage)
                {
                    throw new BuiltinToolException($"{FileWords.TooMany(Rules.MaxFilesPerMessage)} This reply carries that many already.");
                }

                return new Before(null, Math.Max(0, MaxSpaceBytes - await files.GetStoredBytes(call.Tenant.SpaceId, cancellationToken)));
            },
            cancellationToken);

    /// <summary>Reads the file out of the sandbox into the file store, cut off at the file limit or the space's room.</summary>
    private async Task<StoredFile> Copy(
        BuiltinToolCall call,
        string? environment,
        string path,
        string key,
        long room,
        CancellationToken cancellationToken)
    {
        var spaceId = call.Tenant.SpaceId;
        var limit = Math.Min(Rules.MaxFileBytes, room);
        var inspector = new FileInspector();
        var name = FileService.CleanName(path);

        try
        {
            var (size, sha256) = await sandboxes.CopyOut(
                call,
                environment,
                path,
                async content =>
                {
                    await using var metered = new MeteredStream(content, limit, inspector);
                    await store.Save(spaceId, key, metered, cancellationToken);
                    return (metered.BytesRead, metered.FinishHash());
                },
                cancellationToken);
            var facts = inspector.Finish(name);
            return new StoredFile
            {
                Id = call.ToolCallId,
                StorageKey = key,
                Name = name,
                ContentType = facts.ContentType,
                Size = size,
                Sha256 = sha256,
                CreatedByBotId = call.BotId,
                Width = facts.Width,
                Height = facts.Height,
                Preview = facts.Preview,
                CreatedAt = time.GetUtcNow(),
            };
        }
        catch (FileTooLargeException)
        {
            logger.LogInformation("A file bot {BotId} shared went past {MaxBytes} bytes and was refused", call.BotId, limit);
            throw new BuiltinToolException(limit < Rules.MaxFileBytes
                ? FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes)
                : FileWords.TooLarge(null, Rules.MaxFileBytes));
        }
    }

    /// <summary>
    /// Stores the row with the conversation's audience as it is now, counts its bytes against the space, and adds the
    /// file to the reply, in one transaction. Returns the reply as stored.
    /// </summary>
    private async Task<Message> Keep(BuiltinToolCall call, Guid threadId, StoredFile file, string? caption, CancellationToken cancellationToken) =>
        await call.Tenant.Transact(
            async services =>
            {
                var files = services.GetRequiredService<FileRepository>();
                var thread = await services.GetRequiredService<ThreadRepository>().FindThread(threadId, cancellationToken);
                var reply = await services.GetRequiredService<MessageRepository>().GetReplyWithCallForUpdate(threadId, call.RunId, call.ToolCallId, cancellationToken);

                if (thread is null || reply is null)
                {
                    throw new BuiltinToolException("The conversation this file belongs to is gone, so it was not shared.");
                }

                if (!await files.ReserveBytes(call.Tenant.SpaceId, file.Size, MaxSpaceBytes, cancellationToken))
                {
                    throw new BuiltinToolException(FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes));
                }

                file.ThreadId = thread.Id;
                file.AudienceUserId = thread.AudienceUserId;
                file.MessageId = reply.Id;
                files.Add(file);
                reply.Parts = [.. reply.Parts, file.ToPart(caption)];
                return reply;
            },
            cancellationToken);

    /// <summary>One line, bounded, with anything the run knows to be secret taken out. Null when there is none.</summary>
    private static string? CleanCaption(string? caption, Func<string, string> redact)
    {
        var line = string.Join(' ', (caption ?? string.Empty).Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        line = new string([.. line.Where(character => !char.IsControl(character))]);

        if (line.Length == 0)
        {
            return null;
        }

        line = redact(line);
        return line.Length > MaxCaptionLength ? $"{line[..(MaxCaptionLength - 1)]}…" : line;
    }

    #endregion

    #region Nested Types

    private sealed record Before(StoredFile? Shared, long Room);

    #endregion
}
