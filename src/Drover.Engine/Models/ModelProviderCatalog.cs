namespace Drover.Engine.Models;

/// <summary>
/// The model providers this server was built with, and which of them its operator offers. It is built when the
/// server starts, so two providers that claim one kind stop the server there, with a sentence that names the kind.
/// A kind that the deployment policy does not offer is not listed and cannot be given to a new connection. A
/// connection of such a kind stays readable and fails its next call with <see cref="SwitchedOff"/>.
/// </summary>
public sealed class ModelProviderCatalog
{
    #region Private Fields

    private readonly IReadOnlyList<IModelClientProvider> providers;

    private readonly IOptions<DeploymentPolicyOptions> options;

    #endregion

    #region Constructors

    public ModelProviderCatalog(IEnumerable<IModelClientProvider> providers, IOptions<DeploymentPolicyOptions> options)
    {
        this.providers = [.. providers];
        this.options = options;

        if (FindDuplicateKind() is { } kind)
        {
            throw new InvalidOperationException(
                $"Two model providers claim the kind '{kind}'. A kind belongs to one provider: register only one of them.");
        }

        if (FindMismatchedDescriptor() is { } mismatched)
        {
            throw new InvalidOperationException(
                $"The model provider of kind '{mismatched.Kind}' describes itself as '{mismatched.Descriptor.Kind}'. Its descriptor must carry its own kind.");
        }
    }

    #endregion

    #region Properties

    /// <summary>The descriptors of the kinds this server offers, in registration order.</summary>
    public IReadOnlyList<ModelProviderDescriptor> Offered =>
        [.. providers.Where(provider => IsOffered(provider.Kind)).Select(provider => provider.Descriptor)];

    #endregion

    #region Public Methods

    /// <summary>Whether this server offers the kind: it is registered, and the deployment policy allows it.</summary>
    public bool IsOffered(string kind) => options.Value.Rules.OffersModelProvider(kind) && Find(kind) is not null;

    /// <summary>The provider of a kind, whether or not the policy offers it, or null when none is registered.</summary>
    public IModelClientProvider? Find(string kind) => providers.FirstOrDefault(provider => provider.Kind == kind);

    /// <summary>
    /// The provider to call for a connection of this kind, or the sentence to give when it cannot be called: the
    /// operator switched the kind off, or this server has no provider of it.
    /// </summary>
    public Resolution Resolve(string kind) =>
        Find(kind) switch
        {
            null => new Resolution(null, $"This server has no provider of the kind {kind}."),
            _ when !options.Value.Rules.OffersModelProvider(kind) => new Resolution(null, SwitchedOff(kind)),
            var provider => new Resolution(provider, null),
        };

    /// <summary>The refusal for a kind the operator switched off.</summary>
    public static string SwitchedOff(string kind) => $"The operator of this server switched {kind} off.";

    /// <summary>The refusal for a new connection of a kind this server does not offer, whichever the reason.</summary>
    public string NotOffered(string kind) =>
        Find(kind) is null ? $"The kind {kind} is not available on this server." : SwitchedOff(kind);

    #endregion

    #region Private Methods

    private string? FindDuplicateKind() =>
        providers.GroupBy(provider => provider.Kind).FirstOrDefault(group => group.Count() > 1)?.Key;

    private IModelClientProvider? FindMismatchedDescriptor() =>
        providers.FirstOrDefault(provider => provider.Descriptor.Kind != provider.Kind);

    #endregion

    #region Nested Types

    /// <summary>A provider to call, or the sentence that says why there is none.</summary>
    public sealed record Resolution(IModelClientProvider? Provider, string? Refusal);

    #endregion
}
