namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// Every length the native client sets on a field comes from the constant the server's validators read
/// (<see cref="FieldLimits"/>), and every shared limit is set there, so a person is stopped while typing instead of
/// after sending.
/// </summary>
public sealed partial class FieldLimitsTests
{
    #region Public Methods

    [Fact]
    public void Every_limit_on_a_native_field_reads_a_shared_constant()
    {
        var literals = Repository.NativeFiles(".cs", ".xaml")
            .SelectMany(file => NativeLiteralLimit().Matches(file.Text).Select(match => $"{file.Path}: {match.Value}"));

        literals.Should().BeEmpty();
    }

    [Fact]
    public void Every_shared_limit_is_set_in_the_native_client()
    {
        var shared = typeof(FieldLimits)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(int))
            .Select(field => $"{nameof(FieldLimits)}.{field.Name}")
            .ToArray();

        var native = Repository.NativeFiles(".cs", ".xaml")
            .SelectMany(file => SharedConstant().Matches(file.Text).Select(match => match.Value))
            .ToHashSet(StringComparer.Ordinal);

        var missing = shared.Where(name => !native.Contains(name)).ToArray();

        missing.Should().BeEmpty("every shared limit is set in the native client");
    }

    #endregion

    #region Private Methods

    [GeneratedRegex(@"\bFieldLimits\.\w+\b")]
    private static partial Regex SharedConstant();

    [GeneratedRegex(@"MaxLength\s*=\s*""?\d")]
    private static partial Regex NativeLiteralLimit();

    #endregion
}
