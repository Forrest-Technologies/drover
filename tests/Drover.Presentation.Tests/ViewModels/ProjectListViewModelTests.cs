namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The project list against a stand-in server. Every name here is a sample.</summary>
public sealed class ProjectListViewModelTests : ViewModelTest
{
    #region Private Fields

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private static readonly Guid projectId = Guid.Parse("0196f1a0-0000-7000-8000-000000000010");

    private static readonly DateTimeOffset when = DateTimeOffset.Parse("2026-01-02T03:04:05Z");

    #endregion

    #region Public Methods

    [Fact]
    public async Task An_owner_sees_each_project_and_opens_the_one_they_make()
    {
        Answer(role: "owner", projects: [Weekly]);
        var list = Make<ProjectListViewModel>();

        await list.Open();

        list.State.Should().Be(ProjectListState.Listed);
        list.CanManage.Should().BeTrue();
        list.Projects.Should().ContainSingle().Which.Name.Should().Be("Weekly");
        Server.Answer("POST", $"{space}/projects", Weekly);
        await list.Create("  Weekly  ");

        Navigator.Current.Should().Be(Addresses.Project(projectId));
        Server.Sent("POST", $"{space}/projects").Single().Should().Contain("Weekly").And.NotContain("  Weekly");
    }

    [Fact]
    public async Task An_admin_may_make_a_project()
    {
        Answer(role: "admin");
        var list = Make<ProjectListViewModel>();

        await list.Open();

        list.CanManage.Should().BeTrue();
        list.State.Should().Be(ProjectListState.Empty);
    }

    [Fact]
    public async Task An_empty_list_tells_an_owner_what_a_project_is()
    {
        Answer(role: "owner");
        var list = Make<ProjectListViewModel>();

        await list.Open();

        list.State.Should().Be(ProjectListState.Empty);
        ProjectListViewModel.EmptyTitle.Should().Be("No project yet");
        ProjectListViewModel.EmptyCause.Should().Be("A project holds instructions and files that every conversation in it can use.");
        ProjectListViewModel.EmptyAction.Should().Be("New project");
    }

    [Fact]
    public async Task A_member_sees_the_projects_and_is_told_who_makes_one()
    {
        Answer(role: "member", projects: [Weekly]);
        var list = Make<ProjectListViewModel>();

        await list.Open();

        list.CanManage.Should().BeFalse();
        list.Projects.Should().ContainSingle().Which.Name.Should().Be("Weekly");
        ProjectListViewModel.MemberNote.Should().Contain("Owners and admins");
        await list.Create("Weekly");

        list.Problem.Should().Be(ProjectListViewModel.MemberNote);
        Server.Sent("POST", $"{space}/projects").Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_with_no_project_is_told_who_makes_one()
    {
        Answer(role: "member");
        var list = Make<ProjectListViewModel>();

        await list.Open();

        list.State.Should().Be(ProjectListState.Empty);
        ProjectListViewModel.MemberEmptyCause.Should().Be("An owner or admin of the space can make one.");
        ProjectListViewModel.MemberEmptyAction.Should().Be("Back to conversations");
    }

    [Fact]
    public async Task A_blank_name_is_not_sent()
    {
        Answer(role: "owner", projects: [Weekly]);
        var list = Make<ProjectListViewModel>();
        await list.Open();

        await list.Create("   ");

        list.Problem.Should().Be(ProjectListViewModel.NeedsName);
        Server.Sent("POST", $"{space}/projects").Should().BeEmpty();
        Navigator.Current.Should().Be(Addresses.Home);
    }

    [Fact]
    public async Task A_refusal_is_said_in_the_servers_words()
    {
        Answer(role: "owner", projects: [Weekly]);
        Server.Refuse("POST", $"{space}/projects", HttpStatusCode.Forbidden, "Only an owner or admin can make a project.");
        var list = Make<ProjectListViewModel>();
        await list.Open();

        await list.Create("Weekly");

        list.Problem.Should().Be("Only an owner or admin can make a project.");
        list.Working.Should().BeNull();
        Navigator.Current.Should().Be(Addresses.Home);
    }

    [Fact]
    public async Task The_list_says_it_is_loading_and_a_cancelled_load_is_dropped()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/projects", new[] { Weekly });
        var held = Server.Hold("GET", $"{space}/projects");
        var list = Make<ProjectListViewModel>();

        var opening = list.Open();
        list.State.Should().Be(ProjectListState.Loading);
        ProjectListViewModel.Loading.Should().Be("Loading the projects.");
        list.Cancel();
        held.SetResult();
        await opening;

        list.Projects.Should().BeNull();
        await list.Open();
        list.State.Should().Be(ProjectListState.Listed);
        list.Projects.Should().ContainSingle().Which.Name.Should().Be("Weekly");
    }

    [Fact]
    public async Task A_server_that_refuses_the_list_says_so()
    {
        SignedIn();
        Server.Refuse("GET", $"{space}/projects", HttpStatusCode.Forbidden, "Only a member of the space can read its projects.");
        var list = Make<ProjectListViewModel>();

        await list.Open();

        list.State.Should().Be(ProjectListState.Failed);
        ProjectListViewModel.LoadFailedTitle.Should().Be("Drover could not load the projects");
        list.LoadProblem.Should().Be("Only a member of the space can read its projects.");
    }

    #endregion

    #region Helpers

    private static ProjectSummary Weekly { get; } = new(projectId, "Weekly", "Ship the note.", when, when);

    private void Answer(string role, params ProjectSummary[] projects)
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [Person.Spaces[0] with { Role = role }] });
        Server.Answer("GET", $"{space}/projects", projects);
    }

    #endregion
}
