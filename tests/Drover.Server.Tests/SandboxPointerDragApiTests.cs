namespace Drover.Server.Tests;

/// <summary>
/// A touch drag on the sandbox screen reaches the sandbox as the pointer a person would use: the button goes down,
/// the pointer moves with it held, and the button comes up, in that order and never left held. The input is sent as
/// the app sends it, one request per pointer event, to the real route, and the sandbox is a provider that records every
/// command it is asked to run. The requests are plain objects, so a server that does not know the pointer kinds fails
/// these tests by refusing them, not because a type is missing.
/// </summary>
public sealed class SandboxPointerDragApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly MemorySandboxProvider provider = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<ISandboxProvider>(provider);
        services.AddSandboxTools();
    }

    #endregion

    #region Dragging

    [Fact]
    public async Task A_drag_sent_as_down_moves_and_up_reaches_the_sandbox_in_that_order_and_ends_with_the_button_released()
    {
        var (client, input) = await DesktopBot("dragger");
        await client.Post(input, new { kind = "click", x = 5, y = 5 });
        var before = Pointer().Count;

        (await client.Post(input, new { kind = "down", x = 100, y = 120 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.Post(input, new { kind = "move", x = 160, y = 150 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.Post(input, new { kind = "move", x = 240, y = 210 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.Post(input, new { kind = "move", x = 300, y = 260 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.Post(input, new { kind = "up", x = 300, y = 260 })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var events = Pointer().Skip(before).ToList();
        events.Select(pointer => pointer.Kind).Should().Equal(
            PointerKind.Down, PointerKind.Move, PointerKind.Move, PointerKind.Move, PointerKind.Up);
        (events[0].X, events[0].Y).Should().Be((100, 120));
        events[^1].Button.Should().Be("1", "the button that went down is the one let go");
        PointerEvents.Held(events).Should().BeEmpty("a drag ends with every button released");
    }

    [Fact]
    public async Task A_drag_sent_as_one_request_presses_moves_and_releases_and_never_leaves_the_button_held()
    {
        var (client, input) = await DesktopBot("whole");
        var before = Pointer().Count;

        var drag = await client.Post(input, new { kind = "drag", x = 50, y = 60, x2 = 400, y2 = 300 });

        drag.StatusCode.Should().Be(HttpStatusCode.NoContent, await drag.Content.ReadAsStringAsync(Token));
        var events = Pointer().Skip(before).ToList();
        events.First().Kind.Should().Be(PointerKind.Down);
        events.Last().Kind.Should().Be(PointerKind.Up);
        events.Count(pointer => pointer.Kind is PointerKind.Move).Should().BeGreaterThanOrEqualTo(1);
        PointerEvents.Held(events).Should().BeEmpty();
    }

    [Fact]
    public async Task A_point_off_the_screen_is_refused_and_presses_nothing()
    {
        var (client, input) = await DesktopBot("edge");
        var before = Pointer().Count;

        var refused = await client.Post(input, new { kind = "down", x = 5000, y = 10 });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Pointer().Skip(before).Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private List<PointerEvent> Pointer() => PointerEvents.Of(provider.Commands);

    private async Task<(ApiClient Client, string Input)> DesktopBot(string name)
    {
        var person = await SeedPerson(name);
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var space = $"/api/spaces/{person.SpaceId}";
        var connection = await Read<ModelConnectionSummary>(await client.Post(
            $"{space}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")));
        var bot = await Read<BotSummary>(await client.Post($"{space}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)));
        var saved = await client.Put(
            $"{space}/bots/{bot.Id}/definition",
            new UpdateBotRequest(null, null, null, null, null, null, null, null, null, "private", new SandboxSettingsBody("off", [], null, Desktop: true), null, null));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        return (client, $"{space}/bots/{bot.Id}/sandbox/input");
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    #endregion
}
