namespace Drover.Presentation.Tests.Routing;

/// <summary>
/// The project screens name their controls and use buttons, links, and fields, which take the keyboard.
/// A running native host is not what this test starts.
/// </summary>
public sealed class ProjectPageSourceTests
{
    #region Public Methods

    [Fact]
    public void Project_pages_name_their_controls_and_use_keyboard_controls()
    {
        var list = Page("Pages/Projects/ProjectListPage.cs");
        var detail = Page("Pages/Projects/ProjectPage.cs");
        var action = Page("Pages/PageParts.cs");

        list.Should().Contain("AutomationProperties.SetName");
        detail.Should().Contain("AutomationProperties.SetName");
        action.Should().Contain("AutomationProperties.SetName");
        list.Should().Contain("PageParts.Field");
        list.Should().Contain("PageParts.Action");
        detail.Should().Contain("PageParts.Field");
        detail.Should().Contain("new TextArea");
        detail.Should().Contain("PageParts.Action");
        detail.Should().Contain("Launcher.LaunchUriAsync");
        list.Should().NotContain("PointerPressed");
        detail.Should().NotContain("PointerPressed");
        list.Should().Contain("ProjectListViewModel.Loading");
        list.Should().Contain("ProjectListViewModel.LoadFailedTitle");
        list.Should().Contain("ProjectListViewModel.EmptyTitle");
        list.Should().Contain("ProjectListViewModel.MemberEmptyCause");
        detail.Should().Contain("ProjectViewModel.Loading");
        detail.Should().Contain("ProjectViewModel.MissingTitle");
        detail.Should().Contain("ProjectViewModel.RemoveQuestion");
        detail.Should().Contain("ProjectViewModel.MemberNote");
        detail.Should().Contain("ProjectViewModel.NoFiles");
        detail.Should().Contain("ProjectViewModel.NoBot");
    }

    #endregion

    #region Private Methods

    private static string Page(string relative) =>
        File.ReadAllText(Path.Combine(Repository.NativeFolder, relative));

    #endregion
}
