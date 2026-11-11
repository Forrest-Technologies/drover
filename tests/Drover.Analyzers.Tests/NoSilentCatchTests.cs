namespace Drover.Analyzers.Tests;

/// <summary>DRV0005: no silent catch.</summary>
public sealed class NoSilentCatchTests
{
    #region Private Fields

    // Stands in for Microsoft.Extensions.Logging, which the sample compilation does not reference.
    private const string LoggingStub = """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger
            {
            }

            public interface ILogger<T> : ILogger
            {
            }

            public static class LoggerExtensions
            {
                public static void LogError(this ILogger logger, System.Exception exception, string message, params object[] args)
                {
                }
            }
        }

        """;

    #endregion

    #region Non-compliant samples

    [Fact]
    public Task Flags_an_empty_catch() =>
        Verify.Analyzer<NoSilentCatchAnalyzer>("""
            using System;

            public class Worker
            {
                public void Run()
                {
                    try
                    {
                        Console.Write(1);
                    }
                    {|DRV0005:catch|}
                    {
                    }
                }
            }
            """);

    [Fact]
    public Task Flags_a_catch_that_only_has_a_comment() =>
        Verify.Analyzer<NoSilentCatchAnalyzer>("""
            using System;

            public class Worker
            {
                public void Run()
                {
                    try
                    {
                        Console.Write(1);
                    }
                    {|DRV0005:catch|} (Exception)
                    {
                        // ignored
                    }
                }
            }
            """);

    [Fact]
    public Task Flags_a_catch_that_returns_a_value_not_built_from_the_exception() =>
        Verify.Analyzer<NoSilentCatchAnalyzer>("""
            using System;

            public class Worker
            {
                public string Run()
                {
                    try
                    {
                        return "done";
                    }
                    {|DRV0005:catch|} (Exception ex)
                    {
                        return null;
                    }
                }
            }
            """);

    [Fact]
    public Task Flags_a_catch_that_only_throws_inside_a_lambda() =>
        Verify.Analyzer<NoSilentCatchAnalyzer>("""
            using System;

            public class Worker
            {
                public void Run()
                {
                    try
                    {
                        Console.Write(1);
                    }
                    {|DRV0005:catch|} (Exception)
                    {
                        Action later = () => throw new InvalidOperationException();
                    }
                }
            }
            """);

    #endregion

    #region Compliant samples

    [Fact]
    public Task Allows_a_catch_that_logs_and_rethrows() =>
        Verify.Analyzer<NoSilentCatchAnalyzer>($$"""
            using System;
            using Microsoft.Extensions.Logging;

            public class Worker(ILogger<Worker> logger)
            {
                public void Run()
                {
                    try
                    {
                        Console.Write(1);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to do work");
                        throw;
                    }
                }
            }

            {{LoggingStub}}
            """);

    [Fact]
    public Task Allows_a_catch_that_logs_and_handles_the_failure() =>
        Verify.Analyzer<NoSilentCatchAnalyzer>($$"""
            using System;
            using Microsoft.Extensions.Logging;

            public class Worker(ILogger<Worker> logger)
            {
                public bool Run()
                {
                    try
                    {
                        Console.Write(1);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to do work");
                        return false;
                    }
                }
            }

            {{LoggingStub}}
            """);

    [Fact]
    public Task Allows_a_catch_that_throws_a_wrapping_exception() =>
        Verify.Analyzer<NoSilentCatchAnalyzer>("""
            using System;

            public class Worker
            {
                public void Run()
                {
                    try
                    {
                        Console.Write(1);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Could not run", ex);
                    }
                }
            }
            """);

    [Fact]
    public Task Allows_a_catch_that_returns_a_value_built_from_the_exception() =>
        Verify.Analyzer<NoSilentCatchAnalyzer>("""
            using System;

            public class Worker
            {
                public string Run()
                {
                    try
                    {
                        return "done";
                    }
                    catch (Exception ex)
                    {
                        return $"failed: {ex.Message}";
                    }
                }
            }
            """);

    [Fact]
    public Task Allows_a_catch_with_a_filter_that_rethrows() =>
        Verify.Analyzer<NoSilentCatchAnalyzer>("""
            using System;

            public class Worker
            {
                public void Run()
                {
                    try
                    {
                        Console.Write(1);
                    }
                    catch (InvalidOperationException) when (Console.IsOutputRedirected)
                    {
                        throw;
                    }
                }
            }
            """);

    #endregion
}
