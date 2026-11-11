namespace Drover.Adapters.Tests.Agents;

/// <summary>
/// One user in a personal space with a bot, made through the app role the way the product will make them, and the
/// helpers to start runs in it. Every test builds its own, so tests never share rows.
/// </summary>
public sealed class AgentWorld(IServiceProvider services, Guid spaceId, Guid userId, Guid botId)
{
    #region Properties

    public Guid SpaceId => spaceId;

    public Guid UserId => userId;

    public BotDefinition Bot => new()
    {
        BotId = botId,
        Name = "Helper",
        Instructions = "Answer briefly.",
        ModelConnectionId = Guid.Empty,
        Model = "scripted",
    };

    #endregion

    #region Public Methods

    public static async Task<AgentWorld> Create(IServiceProvider services)
    {
        var spaceId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var connection = new ModelConnection { Name = "Scripted", Kind = "openai_compatible" };
        var bot = new Bot
        {
            Name = "Helper",
            Handle = $"helper-{spaceId:N}",
            ModelConnectionId = connection.Id,
            Model = "scripted",
        };

        await Transact(services, spaceId, userId, db =>
        {
            db.Users.Add(new User
            {
                Id = userId,
                UserName = $"user-{userId:N}",
                NormalizedUserName = $"USER-{userId:N}".ToUpperInvariant(),
                Email = $"{userId:N}@example.test",
                NormalizedEmail = $"{userId:N}@EXAMPLE.TEST".ToUpperInvariant(),
                DisplayName = "Test user",
            });
            db.Spaces.Add(new Space { Id = spaceId, Kind = SpaceKind.Personal, Name = "Personal", Slug = $"personal-{spaceId:N}" });
            db.Memberships.Add(new Membership { UserId = userId, Role = MembershipRole.Owner });
            db.ModelConnections.Add(connection);
            db.Bots.Add(bot);
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = bot.Id, DescendantBotId = bot.Id, Distance = 0 });
            return Task.FromResult(true);
        });

        return new AgentWorld(services, spaceId, userId, bot.Id);
    }

    /// <summary>A private thread holding one user message, a queued run for it, and catalog rows for the tools.</summary>
    public async Task<RunExecution> NewRun(string userText, IReadOnlyList<ToolSpec> tools)
    {
        var thread = new ChatThread { BotId = botId, OwnerUserId = userId, AudienceUserId = userId, Title = userText };
        var run = new Drover.Domain.Runs.Run { ThreadId = thread.Id, BotId = botId, PrincipalUserId = userId };
        run.RootRunId = run.Id;
        List<ToolDefinition> definitions = [];

        await Transact(services, spaceId, userId, db =>
        {
            db.Threads.Add(thread);
            db.Messages.Add(new Message
            {
                ThreadId = thread.Id,
                AudienceUserId = userId,
                Position = 1,
                Role = MessageRole.User,
                AuthorUserId = userId,
                Parts = [new TextPart(userText)],
            });
            db.Runs.Add(run);

            foreach (var spec in tools)
            {
                var tool = NewTool(spec);
                db.Tools.Add(tool);
                definitions.Add(new ToolDefinition
                {
                    ToolId = tool.Id,
                    Name = spec.Name,
                    Description = $"The {spec.Name} tool.",
                    InputSchema = tool.InputSchema,
                    Sensitivity = spec.Sensitivity,
                    RequiresApproval = spec.RequiresApproval,
                });
            }

            return Task.FromResult(true);
        });

        return new RunExecution
        {
            RunId = run.Id,
            SpaceId = spaceId,
            ThreadId = thread.Id,
            PrincipalUserId = userId,
            Bot = Bot,
            Tools = definitions,
        };
    }

    /// <summary>The next user message in a thread, as the engine stores it before it queues a run.</summary>
    public Task AddUserMessage(Guid threadId, string text) =>
        Write(async db =>
        {
            var last = await db.Messages.Where(message => message.ThreadId == threadId).MaxAsync(message => message.Position);
            db.Messages.Add(new Message
            {
                ThreadId = threadId,
                AudienceUserId = userId,
                Position = last + 1,
                Role = MessageRole.User,
                AuthorUserId = userId,
                Parts = [new TextPart(text)],
            });
        });

    public Task<T> Read<T>(Func<DroverDbContext, Task<T>> query) => Transact(services, spaceId, userId, query);

    public Task Write(Func<DroverDbContext, Task> change) =>
        Transact(services, spaceId, userId, async db =>
        {
            await change(db);
            return true;
        });

    #endregion

    #region Private Methods

    private static async Task<T> Transact<T>(IServiceProvider services, Guid spaceId, Guid userId, Func<DroverDbContext, Task<T>> work)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin();
        var result = await work(scope.ServiceProvider.GetRequiredService<DroverDbContext>());
        await transaction.Commit();
        return result;
    }

    private static Tool NewTool(ToolSpec spec) => new()
    {
        SourceKind = ToolSourceKind.Builtin,
        Name = spec.Name,
        DefinitionHash = Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())),
        InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone(),
        Sensitivity = spec.Sensitivity,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow,
    };

    #endregion
}
