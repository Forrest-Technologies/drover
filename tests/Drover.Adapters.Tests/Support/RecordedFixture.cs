namespace Drover.Adapters.Tests.Support;

/// <summary>
/// A set of canned HTTP exchanges kept as JSON under Fixtures. Each file says whether it was captured
/// from the live service or written by hand from documentation, and how.
/// </summary>
public sealed class RecordedFixture
{
    #region Properties

    public required string Provenance { get; init; }

    public required string Note { get; init; }

    public required IReadOnlyList<RecordedExchange> Exchanges { get; init; }

    #endregion

    #region Public Methods

    public static RecordedFixture Load(string folder, string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", folder, $"{name}.json");

        return Parse(File.ReadAllText(path));
    }

    public static IEnumerable<string> AllFixturePaths() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.json", SearchOption.AllDirectories);

    public static RecordedFixture Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new RecordedFixture
        {
            Provenance = root.GetProperty("provenance").GetString()!,
            Note = root.GetProperty("note").GetString()!,
            Exchanges = [.. root.GetProperty("exchanges").EnumerateArray().Select(RecordedExchange.Parse)],
        };
    }

    #endregion
}
