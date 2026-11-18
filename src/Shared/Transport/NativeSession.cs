namespace Drover.Transport;

public sealed class NativeSession(ISecureStorage storage, TimeProvider time, ILogger<NativeSession> logger) : IDisposable
{
    #region Private Fields

    private static readonly TimeSpan renewEarly = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim gate = new(1);

    private Uri? loadedOrigin;

    private string? refreshToken;

    private Access? access;

    #endregion

    #region Public Methods

    public void Dispose() => gate.Dispose();

    public async Task<string?> GetAccess(
        Uri origin,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken,
        string? rejectedAccess = null)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (loadedOrigin != origin)
            {
                refreshToken = await Load(origin);
                loadedOrigin = origin;
                access = null;
            }

            if (access is { } current)
            {
                if (current.Token != rejectedAccess && current.ExpiresAt - time.GetUtcNow() > renewEarly)
                {
                    return current.Token;
                }

                access = null;
            }

            if (refreshToken is null)
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "api/native/refresh"))
            {
                Content = NativeSessionTokens.RefreshContent(refreshToken),
            };
            using var response = await send(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Forget(origin);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("Drover could not restore the sign-in.", null, response.StatusCode);
            }

            return await Save(origin, response, cancellationToken)
                ?? throw new HttpRequestException("Drover did not issue a usable native session.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> Exchange(
        Uri origin,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        string antiforgery,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Forget(origin);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "api/native/session"));
            request.Headers.Add(AppRequests.AntiforgeryHeader, antiforgery);
            using var response = await send(request, cancellationToken);
            return response.IsSuccessStatusCode && await Save(origin, response, cancellationToken) is not null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            logger.LogWarning("Drover could not save a native session after sign-in");
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task Clear(Uri origin, string? signedOutAccess)
    {
        await gate.WaitAsync();
        try
        {
            if (signedOutAccess is null || (loadedOrigin == origin && access?.Token == signedOutAccess))
            {
                Forget(origin);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    #endregion

    #region Private Methods

    private async Task<string?> Save(Uri origin, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var issued = await NativeSessionTokens.Read(response.Content, cancellationToken);
        if (issued is null || string.IsNullOrEmpty(issued.AccessToken) || string.IsNullOrEmpty(issued.RefreshToken)
            || issued.ExpiresIn <= 0 || issued.ExpiresIn > 3600)
        {
            return null;
        }

        try
        {
            await storage.SetAsync(Key(origin), issued.RefreshToken);
        }
        catch (Exception)
        {
            logger.LogWarning("The platform could not store the Drover session");
        }

        loadedOrigin = origin;
        refreshToken = issued.RefreshToken;
        access = new Access(issued.AccessToken, time.GetUtcNow().AddSeconds(issued.ExpiresIn));
        return access.Token;
    }

    private async Task<string?> Load(Uri origin)
    {
        try
        {
            return await storage.GetAsync(Key(origin));
        }
        catch (Exception)
        {
            logger.LogWarning("The platform could not read the saved Drover session");
            throw new HttpRequestException("The device could not read the saved Drover session.");
        }
    }

    private void Forget(Uri origin)
    {
        if (loadedOrigin == origin)
        {
            refreshToken = null;
            access = null;
        }

        try
        {
            storage.Remove(Key(origin));
        }
        catch (Exception)
        {
            logger.LogWarning("The platform could not remove the saved Drover session");
        }
    }

    private static string Key(Uri origin) => $"drover.native.refresh:{origin.AbsoluteUri}";

    private sealed record Access(string Token, DateTimeOffset ExpiresAt)
    {
        public override string ToString() => nameof(Access);
    }

    #endregion
}
