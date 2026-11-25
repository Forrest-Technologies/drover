namespace Drover.Engine.Tests.Push;

/// <summary>What authenticated devices may read after a wake, with message text kept out of the wake payload.</summary>
public sealed class PushServiceTests(PostgresServer postgres)
{
    #region Wake Text

    [Fact]
    public async Task Describe_FinishedRun_IncludesBotNameAndTrimmedReplyText()
    {
        var prefix = new string('a', PushService.MaxReplyTextElements - 2);
        var reply = $"{prefix}😀more words that are outside the preview";
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput(reply)]));
        world.StartHost();
        var threadId = await world.NewThread();

        await world.WaitForStatus(await world.Post(threadId, "Give me the long answer."), RunStatus.Completed);
        var wake = await world.WithService<PushService, PushWakeText?>(service =>
            service.Describe(world.UserId, PushKinds.RunFinished, threadId, RunWorld.Token));
        var botName = await world.Read(db => db.Bots
            .Where(bot => bot.Id == world.BotId)
            .Select(bot => bot.Name)
            .SingleAsync(RunWorld.Token));
        var thread = await world.ReadThread(threadId);
        var storedReply = (await world.ReadMessages(threadId)).Last(message => message.Role == MessageRole.Assistant);

        wake.Should().NotBeNull();
        wake!.BotName.Should().Be(botName);
        wake.ReplyText.Should().Be($"{prefix}😀…");
        new StringInfo(wake.ReplyText!).LengthInTextElements.Should().Be(PushService.MaxReplyTextElements);
        wake.ConversationTitle.Should().Be(thread.Title);
        wake.ReplyMessageId.Should().Be(storedReply.Id);
        wake.ReplyCreatedAt.Should().Be(storedReply.CreatedAt);
        wake.Body.Should().Be(PushWords.Sentence(PushKinds.RunFinished, botName));
    }

    [Fact]
    public async Task Describe_OtherUsersThread_ReturnsNull()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Done.")]));
        var member = await world.AddMember(MembershipRole.Member);
        var threadId = await world.NewThread();

        var wake = await world.WithServiceAs<PushService, PushWakeText?>(member, service =>
            service.Describe(member, PushKinds.RunFinished, threadId, RunWorld.Token));

        wake.Should().BeNull("the thread is the owner's private conversation");
    }

    [Fact]
    public void Reply_preview_has_a_hard_utf16_bound_for_one_pathological_grapheme()
    {
        var reply = $"a{new string('\u0301', PushService.MaxReplyTextCodeUnits * 2)}";

        var preview = PushService.PreviewReplyText(reply);

        preview.Should().NotBeNull();
        preview!.Length.Should().BeLessThanOrEqualTo(PushService.MaxReplyTextCodeUnits);
        preview.Should().EndWith("…");
    }

    #endregion
}
