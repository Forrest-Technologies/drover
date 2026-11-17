namespace Drover.App;

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
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (access is { } current && loadedOrigin == origin && current.ExpiresAt - time.GetUtcNow() > renewEarly)
            {
                return current.Token;
            }

            if (loadedOrigin != origin)
            {
                loadedOrigin = origin;
                access = null;
                refreshToken = await Load(origin);
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
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
            {
                Forget(origin);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("Drover could not restore the sign-in.", null, response.StatusCode);
            }

            return await Save(origin, response, cancellationToken);
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

    public async Task Clear(Uri origin, string? rejectedAccess = null)
    {
        await gate.WaitAsync();
        try
        {
            if (rejectedAccess is null || (loadedOrigin == origin && access?.Token == rejectedAccess))
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
            Forget(origin);
            return null;
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
