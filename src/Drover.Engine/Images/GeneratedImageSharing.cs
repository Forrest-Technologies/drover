namespace Drover.Engine.Images;

/// <summary>Keeps one generated image in the reply that asked for it.</summary>
public sealed class GeneratedImageSharing(
    IFileStore store,
    IOptions<DeploymentPolicyOptions> deployment,
    TimeProvider time,
    ILogger<GeneratedImageSharing> logger)
{
    #region Private Fields

    private const int MaxCaptionLength = 200;

    #endregion

    #region Properties

    private DeploymentPolicy Rules => deployment.Value.Rules;

    private long MaxSpaceBytes => Rules.MaxSpaceFileMegabytes * 1024L * 1024;

    private string TooManyFiles =>
        $"{FileWords.TooMany(Rules.MaxFilesPerMessage)} This reply carries that many already.";

    #endregion

    #region Public Methods

    public async Task<StoredFile> Share(
        BuiltinToolCall call,
        Func<int, Task<ModelImageResult>> generate,
        string? caption,
        CancellationToken cancellationToken)
    {
        if (call.ThreadId is not { } threadId)
        {
            throw new BuiltinToolException("An image can be created only in a conversation.");
        }

        if (call.IsDelegated)
        {
            var refusal = string.Join(
                ' ',
                [
                    "Work handed to you by another bot cannot add files to the person's conversation.",
                    "Describe the image needed in your answer so the bot that asked can create it.",
                ]);
            throw new BuiltinToolException(refusal);
        }

        var before = await Look(call, threadId, cancellationToken);

        if (before is { Shared: { } shared, Reply: { } existingReply })
        {
            await call.AnnounceMessage(existingReply);
            return shared;
        }

        var generated = await generate((int)Math.Min(Rules.MaxFileBytes, before.Room));
        var file = await Copy(call, generated, cancellationToken);

        Kept kept;

        try
        {
            kept = await Keep(call, threadId, file, CleanCaption(caption, call.Redact), cancellationToken);
        }
        catch (Exception)
        {
            await store.Delete(call.Tenant.SpaceId, file.StorageKey, CancellationToken.None);
            throw;
        }

        if (!kept.UsedCopy)
        {
            await store.Delete(call.Tenant.SpaceId, file.StorageKey, CancellationToken.None);
        }

        await call.AnnounceMessage(kept.Reply);

        logger.LogInformation(
            "Bot {BotId} created image file {FileId} of {Size} bytes in thread {ThreadId}",
            call.BotId,
            kept.File.Id,
            kept.File.Size,
            threadId);
        return kept.File;
    }

    #endregion

    #region Storage

    private async Task<StoredFile> Copy(
        BuiltinToolCall call,
        ModelImageResult generated,
        CancellationToken cancellationToken)
    {
        var spaceId = call.Tenant.SpaceId;
        var key = FileStorageKeys.For(spaceId, Guid.CreateVersion7());
        var inspector = new FileInspector();
        await using var content = new MemoryStream(generated.Data.ToArray(), writable: false);
        await using var metered = new MeteredStream(content, Rules.MaxFileBytes, inspector);

        try
        {
            await store.Save(spaceId, key, metered, cancellationToken);
            var facts = inspector.Finish("generated-image.png");

            if (facts is not { Kind: FileKind.Image, ContentType: "image/png" })
            {
                throw new BuiltinToolException("The model returned bytes that are not a supported PNG.");
            }

            return new StoredFile
            {
                Id = call.ToolCallId,
                StorageKey = key,
                Name = "generated-image.png",
                ContentType = facts.ContentType,
                Size = metered.BytesRead,
                Sha256 = metered.FinishHash(),
                CreatedByBotId = call.BotId,
                Width = facts.Width,
                Height = facts.Height,
                Preview = facts.Preview,
                CreatedAt = time.GetUtcNow(),
            };
        }
        catch
        {
            await store.Delete(spaceId, key, CancellationToken.None);
            throw;
        }
    }

    private async Task<Kept> Keep(
        BuiltinToolCall call,
        Guid threadId,
        StoredFile file,
        string? caption,
        CancellationToken cancellationToken) =>
        await call.Tenant.Transact(
            async services =>
            {
                var files = services.GetRequiredService<FileRepository>();
                var thread = await services.GetRequiredService<ThreadRepository>()
                    .FindThread(threadId, cancellationToken);
                var reply = await services.GetRequiredService<MessageRepository>()
                    .GetReplyWithCallForUpdate(threadId, call.RunId, call.ToolCallId, cancellationToken);

                if (thread is null || reply is null)
                {
                    throw new BuiltinToolException("The conversation for this image is gone, so it was not kept.");
                }

                if (await files.FindFile(file.Id, cancellationToken) is { } existing)
                {
                    return new Kept(existing, reply, false);
                }

                if (FilesIn(reply) >= Rules.MaxFilesPerMessage)
                {
                    throw new BuiltinToolException(TooManyFiles);
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
                return new Kept(file, reply, true);
            },
            cancellationToken);

    #endregion

    #region Preparation

    private async Task<Before> Look(BuiltinToolCall call, Guid threadId, CancellationToken cancellationToken) =>
        await call.Tenant.Transact(
            async services =>
            {
                var files = services.GetRequiredService<FileRepository>();

                if (await files.FindFile(call.ToolCallId, cancellationToken) is { } shared)
                {
                    var messages = services.GetRequiredService<MessageRepository>();
                    var existingReply = shared.MessageId is { } messageId
                        ? await messages.FindMessage(messageId, cancellationToken)
                        : null;

                    if (existingReply is not { ThreadId: var existingThreadId, RunId: var existingRunId }
                        || existingThreadId != threadId
                        || existingRunId != call.RunId
                        || !existingReply.Parts.OfType<ToolCallPart>().Any(part => part.ToolCallId == call.ToolCallId))
                    {
                        throw new BuiltinToolException("The reply this image belongs to is not in the conversation.");
                    }

                    return new Before(shared, 0, existingReply);
                }

                var reply = await services.GetRequiredService<MessageRepository>()
                    .FindReplyWithCall(threadId, call.RunId, call.ToolCallId, cancellationToken)
                    ?? throw new BuiltinToolException("The reply this image belongs to is not in the conversation.");

                if (FilesIn(reply) >= Rules.MaxFilesPerMessage)
                {
                    throw new BuiltinToolException(TooManyFiles);
                }

                var room = Math.Max(
                    0,
                    MaxSpaceBytes - await files.GetStoredBytes(call.Tenant.SpaceId, cancellationToken));

                if (room == 0)
                {
                    throw new BuiltinToolException(FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes));
                }

                return new Before(null, room, null);
            },
            cancellationToken);

    private static int FilesIn(Message reply) => reply.Parts.Count(part => part is FilePart or ImagePart);

    private static string? CleanCaption(string? caption, Func<string, string> redact)
    {
        var line = string.Join(
            ' ',
            (caption ?? string.Empty).Split(
                (char[])['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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

    private sealed record Before(StoredFile? Shared, long Room, Message? Reply);

    private sealed record Kept(StoredFile File, Message Reply, bool UsedCopy);

    #endregion
}
