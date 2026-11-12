namespace Drover.Analyzers.Tests;

/// <summary>DRV0001: no Async suffix on methods.</summary>
public sealed class NoAsyncSuffixTests
{
    #region Non-compliant samples

    [Fact]
    public Task Flags_an_instance_method_with_the_suffix() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            using System.Threading.Tasks;

            public class ThreadService
            {
                public Task<int> {|DRV0001:GetThreadAsync|}() => Task.FromResult(1);
            }
            """);

    [Fact]
    public Task Flags_a_static_method_and_a_method_in_a_struct() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            using System.Threading.Tasks;

            public static class Helpers
            {
                public static Task {|DRV0001:RunAsync|}() => Task.CompletedTask;
            }

            public struct Holder
            {
                public Task {|DRV0001:SaveAsync|}() => Task.CompletedTask;
            }
            """);

    [Fact]
    public Task Flags_a_member_of_an_interface_declared_in_Drover_and_its_implementation() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            using System.Threading.Tasks;

            public interface IThreadStore
            {
                Task {|DRV0001:SaveAsync|}();
            }

            public class ThreadStore : IThreadStore
            {
                public Task {|DRV0001:SaveAsync|}() => Task.CompletedTask;
            }
            """);

    [Fact]
    public Task Flags_a_method_when_the_only_sync_method_of_that_name_is_in_another_type() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            using System.Threading.Tasks;

            public class Reader
            {
                public int Load() => 1;
            }

            public class Writer
            {
                public Task {|DRV0001:LoadAsync|}() => Task.CompletedTask;
            }
            """);

    #endregion

    #region Compliant samples

    [Fact]
    public Task Allows_a_method_without_the_suffix() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            using System.Threading.Tasks;

            public class ThreadService
            {
                public Task<int> GetThread() => Task.FromResult(1);

                public async Task Save() => await Task.Delay(1);
            }
            """);

    [Fact]
    public Task Allows_an_override_of_a_member_declared_elsewhere() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            public class LoggingStream : MemoryStream
            {
                public override Task FlushAsync(CancellationToken cancellationToken) => base.FlushAsync(cancellationToken);
            }
            """);

    [Fact]
    public Task Allows_an_implicit_implementation_of_an_interface_declared_outside_Drover() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            using System;
            using System.Threading.Tasks;

            public class Lease : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }
            """);

    [Fact]
    public Task Allows_an_explicit_implementation_of_an_interface_declared_outside_Drover() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            using System;
            using System.Threading.Tasks;

            public class Lease : IAsyncDisposable
            {
                ValueTask IAsyncDisposable.DisposeAsync() => default;
            }
            """);

    [Fact]
    public Task Allows_a_method_that_has_a_synchronous_twin_in_the_same_type() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            using System.Threading.Tasks;

            public class ThreadService
            {
                public int GetThread() => 1;

                public Task<int> GetThreadAsync() => Task.FromResult(GetThread());
            }
            """);

    [Fact]
    public Task Allows_a_name_that_only_contains_the_word() =>
        Verify.Analyzer<NoAsyncSuffixAnalyzer>("""
            public class Parser
            {
                public int Asynchronous() => 1;

                public int Sync() => 2;
            }
            """);

    #endregion

    #region Code fix

    [Fact]
    public Task Fix_renames_the_method_and_its_callers() =>
        Verify.CodeFix<NoAsyncSuffixAnalyzer, NoAsyncSuffixCodeFix>(
            """
            using System.Threading.Tasks;

            public class ThreadService
            {
                public Task<int> {|DRV0001:GetThreadAsync|}() => Task.FromResult(1);

                public Task<int> Caller() => GetThreadAsync();
            }
            """,
            """
            using System.Threading.Tasks;

            public class ThreadService
            {
                public Task<int> GetThread() => Task.FromResult(1);

                public Task<int> Caller() => GetThread();
            }
            """);

    #endregion
}
