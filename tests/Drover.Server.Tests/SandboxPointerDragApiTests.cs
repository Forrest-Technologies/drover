namespace Drover.Server.Tests;

/// <summary>
/// A touch drag on the sandbox screen reaches the sandbox as the pointer a person would use: the button goes down,
/// the pointer moves with it held, and the button comes up, in that order and never left held. The input is sent as
/// the app sends it, one request per pointer event, to the real route, and the sandbox is a provider that records
/// every command it is asked to run. The requests are plain objects, so a server that does not know the pointer kinds
/// fails these tests by refusing them, not because a type is missing.
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
    public async Task A_drag_sent_as_down_moves_and_up_reaches_the_sandbox_in_that_order_and_is_released_there()
    {
        var (client, input) = await DesktopBot("dragger");
        var before = Pointer().Count;
        (int X, int Y)[] path = [(100, 120), (160, 150), (240, 210), (300, 260)];

        await Send(client, input, new { kind = "down", x = path[0].X, y = path[0].Y });
        await Send(client, input, new { kind = "move", x = path[1].X, y = path[1].Y });
        await Send(client, input, new { kind = "move", x = path[2].X, y = path[2].Y });
        await Send(client, input, new { kind = "move", x = path[3].X, y = path[3].Y });
        await Send(client, input, new { kind = "up", x = path[3].X, y = path[3].Y });

        var events = Pointer().Skip(before).ToList();
        events.Select(pointer => pointer.Kind).Should().Equal(
            PointerKind.Down, PointerKind.Move, PointerKind.Move, PointerKind.Move, PointerKind.Up);
        events.Select(pointer => (pointer.X, pointer.Y)).Should().Equal(
            [path[0], path[1], path[2], path[3], path[3]], "each event is where the finger was");
        events[0].Button.Should().Be("1", "the left button goes down");
        events[^1].Button.Should().Be("1", "the button that went down is the one let go");
        PointerEvents.Held(events).Should().BeEmpty("a drag ends with every button released");
    }

    [Fact]
    public async Task A_drag_sent_as_one_request_presses_moves_and_releases_where_it_ends_and_leaves_nothing_held()
    {
        var (client, input) = await DesktopBot("whole");
        var before = Pointer().Count;

        var drag = await client.Post(input, new { kind = "drag", x = 50, y = 60, x2 = 400, y2 = 300 });

        drag.StatusCode.Should().Be(HttpStatusCode.NoContent, await drag.Content.ReadAsStringAsync(Token));
        var events = Pointer().Skip(before).ToList();
        events.Should().HaveCountGreaterThanOrEqualTo(3);
        events.Select(pointer => pointer.Kind).Skip(1).SkipLast(1).Should().OnlyContain(
            kind => kind == PointerKind.Move, "between the press and the release the pointer only moves");
        events[0].Should().Be(new PointerEvent(PointerKind.Down, 50, 60, "1"));
        events[^1].Should().Be(new PointerEvent(PointerKind.Up, 400, 300, "1"), "the release is where the drag ends");
        PointerEvents.Held(events).Should().BeEmpty();
    }

    [Fact]
    public async Task A_point_off_the_screen_is_refused_and_presses_nothing()
    {
        var (client, input) = await DesktopBot("edge");
        var before = Pointer().Count;

        var refused = await client.Post(input, new { kind = "down", x = 5000, y = 10 });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(Token)).Should().Contain(
            "off the screen", "the refusal is for the point, not for an unknown kind of input");
        Pointer().Skip(before).Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private List<PointerEvent> Pointer() => PointerEvents.Of(provider.Commands);

    private static async Task Send(ApiClient client, string input, object body)
    {
        var response = await client.Post(input, body);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Token));
    }

    private async Task<(ApiClient Client, string Input)> DesktopBot(string name)
    {
        var person = await SeedPerson(name);
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var space = $"/api/spaces/{person.SpaceId}";
        var connection = await Read<ModelConnectionSummary>(await client.Post(
            $"{space}/model-connections",
            new CreateModelConnectionRequest(
                ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")));
        var bot = await Read<BotSummary>(await client.Post(
            $"{space}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)));
        var saved = await client.Put(
            $"{space}/bots/{bot.Id}/definition",
            new UpdateBotRequest(
                null, null, null, null, null, null, null, null, null, "private",
                new SandboxSettingsBody("off", [], null, Desktop: true), null, null));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));

        // The screen's first picture starts the sandbox, as the app's does, so the input has a sandbox to reach.
        (await client.Post($"{space}/bots/{bot.Id}/sandbox/screen")).StatusCode.Should().Be(HttpStatusCode.OK);
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
