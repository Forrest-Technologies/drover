namespace Drover.Presentation.Routing;

/// <summary>
/// Which screen an address opens. The patterns are the ones <see cref="Drover.Ui.Addresses"/> writes, so every
/// address the product hands out resolves here, with its parameters, its query, and its fragment.
/// </summary>
public sealed class RouteTable<TScreen>
{
    #region Private Fields

    private readonly List<(RouteTemplate Template, TScreen Screen)> routes = [];

    #endregion

    #region Properties

    public IEnumerable<string> Patterns => routes.Select(route => route.Template.Pattern);

    #endregion

    #region Public Methods

    /// <summary>Names the screen for one or more address patterns.</summary>
    public RouteTable<TScreen> Map(TScreen screen, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (routes.Any(route => string.Equals(route.Template.Pattern, pattern, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"The address pattern '{pattern}' is already taken by a screen.");
            }

            routes.Add((new(pattern), screen));
        }

        routes.Sort((left, right) => string.CompareOrdinal(left.Template.Precedence, right.Template.Precedence));
        return this;
    }

    /// <summary>The screen an address opens, or null when no screen is at that address.</summary>
    public RouteMatch<TScreen>? Match(string address)
    {
        var (path, query, fragment) = AddressParts.Split(address);

        foreach (var (template, screen) in routes)
        {
            if (template.Match(path) is { } parameters)
            {
                return new(screen, address, parameters, AddressParts.ReadQuery(query), fragment);
            }
        }

        return null;
    }

    #endregion
}
