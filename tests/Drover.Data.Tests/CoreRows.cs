namespace Drover.Data.Tests;

/// <summary>Valid rows of the core tables for tests that need real data, saved through the entity model.</summary>
public static class CoreRows
{
    #region Public Methods

    public static JsonElement EmptyJson() => JsonDocument.Parse("{}").RootElement.Clone();

    public static ModelConnection NewConnection() => new()
    {
        Name = $"connection-{Guid.NewGuid():N}",
        Kind = "openai_compatible",
    };

    public static Tool NewTool() => new()
    {
        SourceKind = ToolSourceKind.Builtin,
        Name = $"tool-{Guid.NewGuid():N}",
        DefinitionHash = Guid.NewGuid().ToString("N"),
        InputSchema = EmptyJson(),
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow,
    };

    public static Bot NewBot(ModelConnection connection, Bot? parent = null) => new()
    {
        Name = $"bot-{Guid.NewGuid():N}",
        Handle = $"handle-{Guid.NewGuid():N}",
        ModelConnectionId = connection.Id,
        Model = "test-model",
        ParentBotId = parent?.Id,
        Depth = (parent?.Depth ?? -1) + 1,
    };

    /// <summary>A bot, its closure rows to itself and to every ancestor, saved in the session's transaction.</summary>
    public static void AddBot(DroverDbContext db, Bot bot, IReadOnlyList<Bot> ancestorsNearestFirst, bool includeOwnRow = true)
    {
        db.Bots.Add(bot);

        if (includeOwnRow)
        {
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = bot.Id, DescendantBotId = bot.Id, Distance = 0 });
        }

        for (var index = 0; index < ancestorsNearestFirst.Count; index++)
        {
            db.BotAncestors.Add(new BotAncestor
            {
                AncestorBotId = ancestorsNearestFirst[index].Id,
                DescendantBotId = bot.Id,
                Distance = index + 1,
            });
        }
    }

    public static ChatThread NewThread(Bot bot, Guid owner, Guid audience) => new()
    {
        BotId = bot.Id,
        OwnerUserId = owner,
        AudienceUserId = audience,
        Title = "A thread",
    };

    public static Message NewMessage(ChatThread thread, int position, string text) => new()
    {
        ThreadId = thread.Id,
        AudienceUserId = thread.AudienceUserId,
        Position = position,
        Role = MessageRole.User,
        AuthorUserId = thread.OwnerUserId,
        Parts = [new TextPart(text)],
    };

    public static Run NewRun(Bot bot, Guid principal, RunStatus status = RunStatus.Queued)
    {
        var run = new Run
        {
            ThreadId = Guid.CreateVersion7(),
            BotId = bot.Id,
            PrincipalUserId = principal,
            Status = status,
        };
        run.RootRunId = run.Id;
        return run;
    }

    #endregion
}
