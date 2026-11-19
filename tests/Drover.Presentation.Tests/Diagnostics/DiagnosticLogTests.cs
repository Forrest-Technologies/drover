namespace Drover.Presentation.Tests.Diagnostics;

/// <summary>The diagnostic log stays under a fixed size, keeps the newest entries, and survives a refusing store.</summary>
public sealed class DiagnosticLogTests : IDisposable
{
    #region Private Fields

    private static readonly AppDescription app = new("1.2.3", "Test platform");

    private readonly MemoryDiagnosticStore store = new();

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 19, 9, 30, 0, TimeSpan.Zero));

    private readonly string folder = Path.Combine(Path.GetTempPath(), $"drover-diagnostics-{Guid.NewGuid():N}");

    #endregion

    #region Public Methods

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_log_never_holds_more_than_two_parts_and_keeps_the_newest_entries()
    {
        var log = new DiagnosticLog(store, time);

        for (var entry = 0; entry < 5000; entry++)
        {
            log.Write($"entry {entry:D5} {new string('x', 100)}\n");
        }

        store.Texts.Values.Sum(text => text.Length).Should().BeLessThanOrEqualTo(2 * DiagnosticLog.PartLimit);
        var saved = log.Compose(app);
        saved.Length.Should().BeLessThan((2 * DiagnosticLog.PartLimit) + 1024);
        saved.Should().Contain("entry 04999").And.NotContain("entry 00000");
        saved.IndexOf("entry 04000", StringComparison.Ordinal)
            .Should().BeLessThan(saved.IndexOf("entry 04999", StringComparison.Ordinal));
    }

    [Fact]
    public void One_entry_longer_than_a_part_is_cut_to_fit()
    {
        var log = new DiagnosticLog(store, time);

        log.Write(new string('x', 3 * DiagnosticLog.PartLimit));

        store.All.Length.Should().BeLessThanOrEqualTo(DiagnosticLog.PartLimit);
        log.Compose(app).Should().Contain("(cut to fit the log)");
    }

    [Fact]
    public void The_whole_saved_file_stays_under_a_fixed_size_with_a_long_failure()
    {
        var log = new DiagnosticLog(store, time);
        for (var entry = 0; entry < 2000; entry++)
        {
            log.Write($"entry {entry:D5} {new string('x', 100)}\n");
        }

        log.RecordFailure(Nested(depth: 200));

        log.Compose(app).Length.Should().BeLessThan((3 * DiagnosticLog.PartLimit) + 1024);
    }

    [Fact]
    public void Files_in_a_folder_rotate_the_same_way_and_outlive_the_app()
    {
        var first = new DiagnosticLog(new DiagnosticFolder(folder), time);
        for (var entry = 0; entry < 1500; entry++)
        {
            first.Write($"entry {entry:D5} {new string('x', 100)}\n");
        }

        first.RecordFailure(new InvalidOperationException("sample"));

        Directory.GetFiles(folder).Sum(file => new FileInfo(file).Length)
            .Should().BeLessThanOrEqualTo(3 * DiagnosticLog.PartLimit);
        var next = new DiagnosticLog(new DiagnosticFolder(folder), time);
        next.TakeFailureOffer().Should().BeTrue();
        next.Compose(app).Should().Contain("entry 01499").And.Contain(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public void A_store_that_refuses_never_stops_the_app_and_the_saved_file_says_so()
    {
        var log = new DiagnosticLog(store, time);
        store.Refuses = true;

        log.Write("lost\n");
        log.RecordFailure(new InvalidOperationException("sample"));
        store.Refuses = false;
        log.Write("kept\n");

        log.Compose(app).Should().Contain("kept").And.NotContain("lost")
            .And.Contain($"Some entries could not be kept on this device ({typeof(IOException).FullName}).");
    }

    [Fact]
    public void Two_writers_sharing_storage_rotate_without_exceeding_the_limit()
    {
        var first = new DiagnosticLog(store, time);
        var second = new DiagnosticLog(store, time);

        for (var entry = 0; entry < 1500; entry++)
        {
            (entry % 2 == 0 ? first : second).Write($"entry {entry:D5} {new string('x', 100)}\n");
        }

        store.Texts.Values.Should().OnlyContain(part => part.Length <= DiagnosticLog.PartLimit);
        store.Texts.Values.Sum(part => part.Length).Should().BeLessThanOrEqualTo(2 * DiagnosticLog.PartLimit);

        // Even if storage already holds an oversized part from an external writer, rotation keeps only PartLimit.
        store.Write("diagnostics", new string('z', DiagnosticLog.PartLimit + 5000));
        first.Write("fresh\n");
        store.Texts["diagnostics-older"].Length.Should().BeLessThanOrEqualTo(DiagnosticLog.PartLimit);
        store.Texts["diagnostics"].Should().Be("fresh\n");

        var diskFirst = new DiagnosticLog(new DiagnosticFolder(folder), time);
        var diskSecond = new DiagnosticLog(new DiagnosticFolder(folder), time);
        for (var entry = 0; entry < 1500; entry++)
        {
            (entry % 2 == 0 ? diskFirst : diskSecond).Write($"entry {entry:D5} {new string('x', 100)}\n");
        }

        Directory.GetFiles(folder).Sum(file => new FileInfo(file).Length)
            .Should().BeLessThanOrEqualTo(2 * DiagnosticLog.PartLimit);
    }

    [Fact]
    public void An_empty_log_says_so()
    {
        var saved = new DiagnosticLog(store, time).Compose(app);

        saved.Should().Contain("None recorded.")
            .And.Contain("Nothing logged.")
            .And.Contain("Saved: 2026-11-19T09:30:00Z");
    }

    #endregion

    #region Private Methods

    private static Exception Nested(int depth)
    {
        Exception failure = new InvalidOperationException("sample");
        for (var level = 0; level < depth; level++)
        {
            failure = new InvalidOperationException("sample", failure);
        }

        return failure;
    }

    #endregion
}
