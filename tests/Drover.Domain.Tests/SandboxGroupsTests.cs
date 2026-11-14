namespace Drover.Domain.Tests;

/// <summary>Which sandbox a bot works in, and who else may: the sharing rules as a table.</summary>
public sealed class SandboxGroupsTests
{
    #region Private Fields

    private static readonly Guid top = Guid.CreateVersion7();

    private static readonly Guid middle = Guid.CreateVersion7();

    private static readonly Guid bottom = Guid.CreateVersion7();

    private static readonly Guid sibling = Guid.CreateVersion7();

    #endregion

    #region Owner Of

    [Fact]
    public void A_private_bot_owns_its_own_sandbox()
    {
        SandboxGroups.OwnerOf(bottom, Path(SandboxPolicy.Private, SandboxPolicy.Private, SandboxPolicy.Private)).Should().Be(bottom);
    }

    [Fact]
    public void A_bot_below_a_subtree_bot_works_in_that_bots_sandbox_whatever_it_says_for_itself()
    {
        SandboxGroups.OwnerOf(bottom, Path(SandboxPolicy.Private, SandboxPolicy.Subtree, SandboxPolicy.Private)).Should().Be(middle);
        SandboxGroups.OwnerOf(bottom, Path(SandboxPolicy.Named, SandboxPolicy.Subtree, SandboxPolicy.None)).Should().Be(middle);
    }

    [Fact]
    public void In_nested_subtree_shares_the_highest_subtree_bot_wins()
    {
        SandboxGroups.OwnerOf(bottom, Path(SandboxPolicy.Subtree, SandboxPolicy.Private, SandboxPolicy.Subtree)).Should().Be(top);
        SandboxGroups.OwnerOf(bottom, Path(SandboxPolicy.Subtree, SandboxPolicy.Subtree, SandboxPolicy.Subtree)).Should().Be(top);
    }

    [Fact]
    public void A_named_owner_and_a_bot_beneath_it_each_own_their_own_sandbox()
    {
        SandboxGroups.OwnerOf(bottom, Path(SandboxPolicy.Private, SandboxPolicy.Named, SandboxPolicy.Private)).Should().Be(bottom);
        SandboxGroups.OwnerOf(middle, Path(SandboxPolicy.Private, SandboxPolicy.Named)).Should().Be(middle);
    }

    [Fact]
    public void A_removed_bot_is_not_on_the_path_so_its_subtree_share_ends()
    {
        // The repository leaves removed bots out of the path; what is left decides.
        List<SandboxPathEntry> withoutRemovedTop = [new(bottom, 0, SandboxPolicy.Private), new(middle, 1, SandboxPolicy.Private)];

        SandboxGroups.OwnerOf(bottom, withoutRemovedTop).Should().Be(bottom);
    }

    #endregion

    #region May Enter

    [Fact]
    public void A_bot_may_enter_the_sandbox_of_its_own_group()
    {
        SandboxGroups.MayEnter(bottom, middle, middle, SandboxPolicy.Subtree, []).Should().BeTrue();
        SandboxGroups.MayEnter(bottom, bottom, bottom, SandboxPolicy.Private, []).Should().BeTrue();
    }

    [Fact]
    public void A_sibling_or_a_parent_may_not_enter_a_private_sandbox()
    {
        SandboxGroups.MayEnter(sibling, sibling, bottom, SandboxPolicy.Private, []).Should().BeFalse();
        SandboxGroups.MayEnter(middle, middle, bottom, SandboxPolicy.Private, []).Should().BeFalse();
    }

    [Fact]
    public void A_named_share_lets_in_exactly_the_bots_it_lists()
    {
        SandboxGroups.MayEnter(sibling, sibling, top, SandboxPolicy.Named, [sibling]).Should().BeTrue();
        SandboxGroups.MayEnter(bottom, bottom, top, SandboxPolicy.Named, [sibling]).Should().BeFalse();
    }

    [Fact]
    public void A_list_of_shares_means_nothing_unless_the_owner_shares_by_name()
    {
        SandboxGroups.MayEnter(sibling, sibling, top, SandboxPolicy.Private, [sibling]).Should().BeFalse();
        SandboxGroups.MayEnter(sibling, sibling, top, SandboxPolicy.Subtree, [sibling]).Should().BeFalse();
    }

    [Fact]
    public void Nobody_enters_the_sandbox_of_a_bot_that_has_none()
    {
        SandboxGroups.MayEnter(top, top, top, SandboxPolicy.None, []).Should().BeFalse();
    }

    #endregion

    #region Network

    [Theory]
    [InlineData(NetworkAccess.Off, 0)]
    [InlineData(NetworkAccess.Allowlist, 2)]
    [InlineData(NetworkAccess.Open, 0)]
    public void The_network_follows_the_settings_and_hosts_count_only_for_an_allowlist(NetworkAccess access, int hosts)
    {
        var settings = new SandboxSettings { Network = access, AllowedHosts = ["example.com", "pypi.org"] };

        var network = settings.ToNetworkPolicy();

        network.Access.Should().Be(access);
        network.AllowedHosts.Should().HaveCount(hosts);
    }

    #endregion

    #region Private Methods

    /// <summary>The path from the bottom bot (distance 0) up, with the policies given top first.</summary>
    private static List<SandboxPathEntry> Path(params SandboxPolicy[] topFirst)
    {
        Guid[] bots = [top, middle, bottom];
        var used = bots[^topFirst.Length..];
        return [.. used.Select((bot, index) => new SandboxPathEntry(bot, used.Length - 1 - index, topFirst[index])).Reverse()];
    }

    #endregion
}
