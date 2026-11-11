namespace Drover.Analyzers.Tests;

/// <summary>DRV0003: regions required on non-trivial types, in the standard order, with padding.</summary>
public sealed class RegionsRequiredTests
{
    #region Non-compliant samples

    [Fact]
    public Task Flags_a_type_with_more_than_three_members_and_no_regions() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public class {|DRV0003:ThreadService|}
            {
                public int First() => 1;

                public int Second() => 2;

                public int Third() => 3;

                public int Fourth() => 4;
            }
            """);

    [Fact]
    public Task Flags_a_type_with_more_than_one_kind_of_member_and_no_regions() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public struct {|DRV0003:Counter|}
            {
                private int count;

                public int Next() => count + 1;
            }
            """);

    [Fact]
    public Task Flags_a_region_that_is_out_of_the_standard_order() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public class ThreadService
            {
                #region Public Methods

                public int Next() => count + 1;

                #endregion

                {|DRV0003:#region Private Fields|}

                private int count;

                #endregion
            }
            """);

    [Fact]
    public Task Flags_a_missing_blank_line_after_the_opening_tag() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public class ThreadService
            {
                {|DRV0003:#region Private Fields|}
                private int count;

                #endregion

                #region Public Methods

                public int Next() => count + 1;

                #endregion
            }
            """);

    [Fact]
    public Task Flags_a_missing_blank_line_before_the_closing_tag() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public class ThreadService
            {
                #region Private Fields

                private int count;
                {|DRV0003:#endregion|}

                #region Public Methods

                public int Next() => count + 1;

                #endregion
            }
            """);

    [Fact]
    public Task Flags_a_member_that_sits_outside_every_region() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public class ThreadService
            {
                #region Private Fields

                private int count;

                #endregion

                {|DRV0003:public|} int Next() => count + 1;
            }
            """);

    #endregion

    #region Compliant samples

    [Fact]
    public Task Allows_an_interface_with_many_members_and_no_regions() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public interface IThreadStore
            {
                int First();

                int Second();

                int Third();

                int Fourth();

                int Count { get; }
            }
            """);

    [Fact]
    public Task Allows_an_enum_and_a_positional_record_without_regions() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public enum RunStatus
            {
                Queued,
                Running,
                Completed,
                Failed,
            }

            public record ThreadId(int Value)
            {
                public int Next() => Value + 1;

                public int Previous() => Value - 1;

                public override string ToString() => $"{Value}";

                public int Twice() => Value * 2;
            }
            """);

    [Fact]
    public Task Allows_a_type_with_three_members_of_one_kind() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public static class ThreadExtensions
            {
                public static int First(this int value) => value;

                public static int Second(this int value) => value;

                public static int Third(this int value) => value;
            }
            """);

    [Fact]
    public Task Allows_regions_in_the_standard_order_with_a_named_group_after_the_public_section() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public class ThreadService(int seed)
            {
                #region Private Fields

                private int count;

                #endregion

                #region Properties

                public int Seed => seed;

                #endregion

                #region Public Methods

                public int Next() => Advance();

                #endregion

                #region Grant Resolution

                private int Advance() => count + seed;

                #endregion
            }
            """);

    [Fact]
    public Task Allows_the_traditional_constructor_layout() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public class ThreadService
            {
                #region Private Fields

                private readonly int seed;

                #endregion

                #region Constructors

                public ThreadService(int seed)
                {
                    this.seed = seed;
                }

                #endregion

                #region Interface Implementations

                public override string ToString() => $"{seed}";

                #endregion
            }
            """);

    [Fact]
    public Task Allows_a_nested_type_that_has_its_own_regions() =>
        Verify.Analyzer<RegionsRequiredAnalyzer>("""
            public class ThreadService
            {
                #region Public Methods

                public int Next() => new Counter().Value();

                #endregion

                #region Nested Types

                private sealed class Counter
                {
                    #region Private Fields

                    private int count;

                    #endregion

                    #region Public Methods

                    public int Value() => count;

                    #endregion
                }

                #endregion
            }
            """);

    #endregion
}
