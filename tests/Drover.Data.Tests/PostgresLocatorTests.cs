namespace Drover.Data.Tests;

/// <summary>
/// The locator is pure apart from one short connection check. These tests inject the start function, so they run
/// without a PostgreSQL server and are outside the database-backed collection.
/// </summary>
public sealed class PostgresLocatorTests
{
    #region Public Methods

    [Fact]
    public async Task A_named_server_is_checked_without_printing_the_password()
    {
        var locator = new PostgresLocator(
            () => "Host=127.0.0.1;Port=1;Username=someone;Password=marker-password-42",
            _ => Task.FromResult("not used"));

        var failure = await FluentActions.Awaiting(() => locator.Find(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<TestEnvironmentException>();

        failure.Which.Message.Should().Contain("127.0.0.1")
            .And.Contain("someone")
            .And.NotContain("marker-password-42");
    }

    [Fact]
    public async Task A_container_failure_says_what_was_tried_and_how_to_fix_it()
    {
        var starts = 0;
        var locator = new PostgresLocator(
            () => null,
            _ =>
            {
                starts++;
                throw new TaskCanceledException();
            });

        var first = await FluentActions.Awaiting(() => locator.Find(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<TestEnvironmentException>();
        var second = await FluentActions.Awaiting(() => locator.Find(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<TestEnvironmentException>();

        first.Which.Message.Should().Contain("DROVER_TEST_POSTGRES is not set")
            .And.Contain("postgres:16-alpine")
            .And.Contain("Docker")
            .And.Contain("dev-postgres.sh");
        first.Which.InnerException.Should().BeOfType<TaskCanceledException>();
        second.Which.Should().BeSameAs(first.Which);
        starts.Should().Be(1);
    }

    [Fact]
    public async Task Two_callers_sharing_an_attempt_start_only_one_connection()
    {
        var attempt = new PostgresAttempt();
        var starts = 0;
        var firstLocator = new PostgresLocator(
            () => null,
            _ =>
            {
                starts++;
                return Task.FromResult("Host=fake;Username=fake");
            },
            attempt);
        var secondLocator = new PostgresLocator(
            () => null,
            _ =>
            {
                starts++;
                return Task.FromResult("Host=other;Username=other");
            },
            attempt);

        var both = await Task.WhenAll(
            firstLocator.Find(TestContext.Current.CancellationToken),
            secondLocator.Find(TestContext.Current.CancellationToken));

        both[0].Should().Be(both[1]);
        starts.Should().Be(1);
    }

    [Fact]
    public async Task Two_callers_sharing_an_attempt_get_the_same_failure()
    {
        var attempt = new PostgresAttempt();
        var starts = 0;
        var locator = new PostgresLocator(
            () => null,
            _ =>
            {
                starts++;
                throw new TaskCanceledException();
            },
            attempt);

        var failures = await Task.WhenAll(
            FluentActions.Awaiting(() => locator.Find(TestContext.Current.CancellationToken)).Should().ThrowAsync<TestEnvironmentException>(),
            FluentActions.Awaiting(() => locator.Find(TestContext.Current.CancellationToken)).Should().ThrowAsync<TestEnvironmentException>());

        failures[0].Which.Should().BeSameAs(failures[1].Which);
        starts.Should().Be(1);
    }

    [Fact]
    public async Task A_successful_lookup_is_remembered_without_starting_twice()
    {
        var starts = 0;
        var locator = new PostgresLocator(
            () => null,
            _ =>
            {
                starts++;
                return Task.FromResult("Host=fake;Username=fake");
            });

        var first = await locator.Find(TestContext.Current.CancellationToken);
        var second = await locator.Find(TestContext.Current.CancellationToken);

        first.Should().Be(second);
        starts.Should().Be(1);
    }

    #endregion
}
