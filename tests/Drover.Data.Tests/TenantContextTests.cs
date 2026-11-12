namespace Drover.Data.Tests;

public sealed class TenantContextTests
{
    #region Public Methods

    [Fact]
    public void A_new_context_has_no_tenant()
    {
        var tenant = new TenantContext();

        tenant.SpaceId.Should().BeNull();
        tenant.UserId.Should().BeNull();
    }

    [Fact]
    public void The_space_cannot_change_once_it_is_set()
    {
        var tenant = new TenantContext();
        tenant.SetSpace(Guid.CreateVersion7());

        var change = () => tenant.SetSpace(Guid.CreateVersion7());

        change.Should().Throw<InvalidOperationException>().WithMessage("*space*cannot change*");
    }

    [Fact]
    public void Setting_the_same_space_again_is_allowed()
    {
        var tenant = new TenantContext();
        var space = Guid.CreateVersion7();
        tenant.SetSpace(space);

        var again = () => tenant.SetSpace(space);

        again.Should().NotThrow();
        tenant.SpaceId.Should().Be(space);
    }

    [Fact]
    public void The_user_cannot_change_once_it_is_set()
    {
        var tenant = new TenantContext();
        tenant.SetUser(Guid.CreateVersion7());

        var change = () => tenant.SetUser(Guid.CreateVersion7());

        change.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_empty_id_is_not_a_tenant()
    {
        var tenant = new TenantContext();

        var space = () => tenant.SetSpace(Guid.Empty);
        var user = () => tenant.SetUser(Guid.Empty);

        space.Should().Throw<ArgumentOutOfRangeException>();
        user.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion
}
