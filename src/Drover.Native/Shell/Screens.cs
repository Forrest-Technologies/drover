namespace Drover.Native.Shell;

/// <summary>
/// Which screen each address opens: the one place a screen is given its address. The patterns are the ones the web
/// client's pages declare and <see cref="Addresses"/> writes, so every address the product hands out opens here,
/// with its parameters and its query. A screen that has moved to this app replaces its placeholder class in
/// <c>Pages/</c>; this table does not change when it does.
/// </summary>
public static class Screens
{
    public static RouteTable<Type> Table { get; } = new RouteTable<Type>()
        .Map(
            typeof(TalkPage),
            "/",
            "/talk/{BotId:guid}",
            "/talk/{BotId:guid}/{Rail}",
            "/talk/{BotId:guid}/side/{SideChat}",
            "/talk/{BotId:guid}/activity/{ActivityRunId:guid}",
            "/talk/{BotId:guid}/work/{WorkBotId:guid}/{WorkThreadId:guid}",
            "/threads/{FormerThreadId:guid}")
        .Map(typeof(SignInPage), "/sign-in")
        .Map(typeof(CreateAccountPage), "/setup")
        .Map(typeof(ConnectModelPage), "/setup/model")
        .Map(typeof(MeetBotPage), "/setup/bot")
        .Map(typeof(ShareToPage), "/share")
        .Map(typeof(BotListPage), "/bots")
        .Map(typeof(ProjectListPage), "/projects")
        .Map(typeof(ProjectPage), "/projects/{ProjectId:guid}")
        .Map(typeof(BotEditorPage), "/bots/{BotId:guid}", "/bots/new")
        .Map(typeof(BotSandboxPage), "/bots/{BotId:guid}/sandbox")
        .Map(typeof(ModelConnectionsPage), "/models")
        .Map(typeof(ToolConnectionsPage), "/tools")
        .Map(typeof(ExtensionsPage), "/extensions")
        .Map(typeof(StatusPage), "/status")
        .Map(typeof(UsagePage), "/usage")
        .Map(typeof(VoicePage), "/voice")
        .Map(typeof(GalleryPage), "/dev/gallery");
}
