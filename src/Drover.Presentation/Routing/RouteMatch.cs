namespace Drover.Presentation.Routing;

/// <summary>
/// An address taken apart: the screen it names, the parameters in its path, what follows the question mark, and
/// what follows the hash. Every value is text, as it stood in the address, unescaped.
/// </summary>
public sealed record RouteMatch<TScreen>(
    TScreen Screen,
    string Address,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyDictionary<string, string> Query,
    string? Fragment)
{
    #region Public Methods

    public Guid? GuidParameter(string name) =>
        Parameters.TryGetValue(name, out var value) && Guid.TryParse(value, out var id) ? id : null;

    public string? Parameter(string name) => Parameters.GetValueOrDefault(name);

    public string? QueryValue(string name) => Query.GetValueOrDefault(name);

    #endregion
}
