namespace Drover.Analyzers.Tests;

public sealed class TenancyShapeAnalyzerTests
{
    #region Private Fields

    private const string TenantManifest =
        """
        # Tables that belong to no space and no user.
        spaces  Space      # the tenant itself
        __EFMigrationsHistory
        """;

    #endregion

    #region Non-Compliant

    [Fact]
    public async Task A_DbSet_of_an_entity_with_no_ownership_interface_is_reported()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class Widget { public int Id { get; set; } }

            public sealed class AppContext : DbContext
            {
                public DbSet<Widget> Widgets { get; } = new();
            }
            """);

        var reported = diagnostics.Should().ContainSingle().Which;
        reported.Id.Should().Be("DRV0006");
        reported.Severity.Should().Be(DiagnosticSeverity.Error);
        reported.GetMessage().Should().Contain("Widget").And.Contain("Widgets").And.Contain("GlobalTables.manifest");
    }

    [Fact]
    public async Task An_entity_is_reported_when_the_manifest_lists_only_other_entities()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class Space { }
            public sealed class Widget { }

            public sealed class AppContext : DbContext
            {
                public DbSet<Space> Spaces { get; } = new();
                public DbSet<Widget> Widgets { get; } = new();
            }
            """,
            TenantManifest);

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Contain("Widget");
    }

    [Fact]
    public async Task Naming_a_table_in_the_manifest_without_the_entity_does_not_allow_a_DbSet()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class Widget { }

            public sealed class AppContext : DbContext
            {
                public DbSet<Widget> Widgets { get; } = new();
            }
            """,
            "widgets  # a table, but no entity named");

        diagnostics.Should().ContainSingle();
    }

    [Fact]
    public async Task An_entity_named_only_in_a_comment_is_still_reported()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class Widget { }

            public sealed class AppContext : DbContext
            {
                public DbSet<Widget> Widgets { get; } = new();
            }
            """,
            "# widgets  Widget");

        diagnostics.Should().ContainSingle();
    }

    [Fact]
    public async Task Every_unowned_DbSet_is_reported_once()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class First { }
            public sealed class Second { }

            public sealed class AppContext : DbContext
            {
                public DbSet<First> Firsts { get; } = new();
                public DbSet<Second> Seconds { get; } = new();
            }
            """);

        diagnostics.Should().HaveCount(2);
    }

    #endregion

    #region Compliant

    [Fact]
    public async Task A_space_owned_entity_is_clean()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;
            using Drover.Domain.Tenancy;

            public sealed class Thread : ISpaceOwned { public System.Guid SpaceId { get; set; } }

            public sealed class AppContext : DbContext
            {
                public DbSet<Thread> Threads { get; } = new();
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task A_user_owned_entity_is_clean()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;
            using Drover.Domain.Tenancy;

            public sealed class Session : IUserOwned { public System.Guid UserId { get; set; } }

            public sealed class AppContext : DbContext
            {
                public DbSet<Session> Sessions { get; } = new();
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task An_interface_inherited_from_a_base_class_counts()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;
            using Drover.Domain.Tenancy;

            public abstract class SpaceRow : ISpaceOwned { public System.Guid SpaceId { get; set; } }
            public sealed class Bot : SpaceRow { }

            public sealed class AppContext : DbContext
            {
                public DbSet<Bot> Bots { get; } = new();
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task An_entity_listed_in_the_manifest_is_clean()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class Space { }

            public sealed class AppContext : DbContext
            {
                public DbSet<Space> Spaces { get; } = new();
            }
            """,
            TenantManifest);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task A_DbSet_property_on_a_class_that_is_not_a_DbContext_is_not_checked()
    {
        var diagnostics = await Run(
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class Widget { }

            public sealed class NotAContext
            {
                public DbSet<Widget> Widgets { get; } = new();
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task A_context_with_no_DbSet_is_clean()
    {
        var diagnostics = await Run("public sealed class AppContext : Microsoft.EntityFrameworkCore.DbContext { }");

        diagnostics.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static Task<ImmutableArray<Diagnostic>> Run(string source, string? manifest = null) =>
        AnalyzerHarness.Run(new TenancyShapeAnalyzer(), source, manifest);

    #endregion
}
