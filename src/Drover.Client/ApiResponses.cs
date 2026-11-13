namespace Drover.Client;

/// <summary>
/// Turns a response into its value, or into the <see cref="DroverApiException"/> that says why it was refused.
/// </summary>
internal static class ApiResponses
{
    #region Public Methods

    public static async Task<T> ReadValue<T>(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await response.ThrowIfRefused(cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new DroverApiException(response.StatusCode, "The server answered with nothing.");
    }

    /// <summary>The value, or nothing when the server answers with <paramref name="absent"/>.</summary>
    public static async Task<T?> ReadValueUnless<T>(
        this HttpResponseMessage response,
        HttpStatusCode absent,
        CancellationToken cancellationToken)
        where T : class =>
        response.StatusCode == absent ? null : await response.ReadValue<T>(cancellationToken);

    public static async Task ThrowIfRefused(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await DroverApiException.From(response, cancellationToken);
        }
    }

    #endregion
}
