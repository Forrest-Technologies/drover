namespace Drover.Server.Models;

/// <summary>
/// The kinds of model provider this server offers, for every client to draw its connection form from. It is the same
/// for everyone who is signed in, so it is not in a space, and it carries no key, header, address, or balance.
/// </summary>
public static class ModelProviderKindEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapModelProviderKindEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/model-provider-kinds", ListKinds)
            .RequireAuthorization()
            .WithName("ListModelProviderKinds")
            .WithSummary("The kinds of model provider this server offers and what a connection of each asks for.");
        return api;
    }

    #endregion

    #region Endpoints

    private static Ok<List<ModelProviderKindResponse>> ListKinds(ModelProviderCatalog catalog) =>
        TypedResults.Ok(catalog.Offered.Select(descriptor => descriptor.ToResponse()).ToList());

    #endregion
}
