namespace Drover.App.Platforms.Android;

public sealed class AndroidPushRegistration(
    ServerAddress server,
    ISecureStorage storage,
    IServiceScopeFactory scopes,
    ILogger<AndroidPushRegistration> logger) : IPushRegistration, IDeviceNotificationSettings, IDisposable
{
    #region Private Fields

    private const string PermissionAsked = "drover.push.permission-asked";

    private readonly SemaphoreSlim gate = new(1);

    private TaskCompletionSource<PushDeviceRequest?>? pending;

    #endregion

    #region Interface Implementations

    public Task<DevicePushCheck> Check(CancellationToken cancellationToken = default) => AsDeviceCall(ReadCheck);

    public Task<PushDeviceRequest?> Subscribe(string publicKey, CancellationToken cancellationToken = default) =>
        AsDeviceCall(() => SubscribeOnDevice(publicKey, cancellationToken));

    public Task<string?> Unsubscribe(CancellationToken cancellationToken = default) =>
        AsDeviceCall(() => UnsubscribeOnDevice(cancellationToken));

    public void Dispose() => gate.Dispose();

    #endregion

    #region Public Methods

    public void OpenNotificationSettings() => AndroidNotifications.OpenSettings();

    public async Task OpenBatterySettings()
    {
        var saved = await Read(server.Current);
        var target = saved?.Distributor;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            using var intent = target is null
                ? new Intent(global::Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings)
                : new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings, AndroidUri.Parse($"package:{target}"));
            Platform.CurrentActivity?.StartActivity(intent);
        });
    }

    internal async Task Receive(Intent intent, string? sender, CancellationToken cancellationToken)
    {
        if (server.Current is not { } origin)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        AndroidPushSubscription? saved;
        try
        {
            saved = await Read(origin);
            if (saved is null || intent.GetStringExtra("token") != saved.Token
                || (sender is not null && sender != saved.Distributor))
            {
                return;
            }

            if (intent.Action != UnifiedPushTransport.Message)
            {
                await ReceiveRegistration(origin, saved, intent, cancellationToken);
                return;
            }

            UnifiedPushTransport.Acknowledge(saved, intent);
        }
        finally
        {
            gate.Release();
        }

        if (intent.GetByteArrayExtra("bytesMessage") is { Length: > 0 and <= 4096 } payload)
        {
            await using var scope = scopes.CreateAsyncScope();
            var messages = scope.ServiceProvider.GetRequiredService<AndroidPushMessages>();
            await messages.Show(saved, payload, cancellationToken);
        }
    }

    internal async Task<bool> BelongsTo(Guid user, string token) =>
        await Read(server.Current) is { } saved && saved.UserId == user && saved.Token == token;

    #endregion

    #region Device Calls

    // Android's own failures are Java exceptions. The notifications control knows a failed device call by one type.
    private static async Task<T> AsDeviceCall<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Java.Lang.Exception exception)
        {
            throw new DeviceCallFailedException("Android could not change notifications for this device.", exception);
        }
    }

    private async Task<DevicePushCheck> ReadCheck()
    {
        var saved = await Reconcile(server.Current);
        var requiresPermission = OperatingSystem.IsAndroidVersionAtLeast(33) && !Preferences.Default.Get(PermissionAsked, false);
        var support = !AndroidNotifications.Allowed && !requiresPermission
            ? DevicePushSupport.Blocked
            : UnifiedPushTransport.Distributors().Count == 0 ? DevicePushSupport.NeedsDistributor : DevicePushSupport.Available;
        return new(support, saved?.Endpoint);
    }

    // A distributor that rotated its address while the server could not be reached leaves the server holding a dead
    // one. The new address is kept at once, so what comes to it can be opened, and the server is told here, whenever the
    // notifications control looks, until it has been.
    private async Task<AndroidPushSubscription?> Reconcile(Uri? origin)
    {
        var saved = await Read(origin);
        if (origin is null || saved is not { HasUnreportedEndpoint: true })
        {
            return saved;
        }

        await gate.WaitAsync();
        try
        {
            saved = await Read(origin);
            if (saved is { HasUnreportedEndpoint: true })
            {
                saved = await Report(origin, saved, CancellationToken.None);
            }

            return saved;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PushDeviceRequest?> SubscribeOnDevice(string publicKey, CancellationToken cancellationToken)
    {
        if (server.Current is not { } origin || !await AllowNotifications())
        {
            return null;
        }

        var distributor = await UnifiedPushTransport.Choose();
        if (distributor is null)
        {
            return null;
        }

        await using var scope = scopes.CreateAsyncScope();
        var api = scope.ServiceProvider.GetRequiredService<DroverApi>();
        if (await api.GetSession(cancellationToken) is not { } user)
        {
            return null;
        }

        var request = await Begin(origin, distributor, user.UserId, publicKey, cancellationToken);
        try
        {
            return await request.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("The UnifiedPush distributor did not return an endpoint");
            await UnsubscribeOnDevice(cancellationToken);
            return null;
        }
    }

    private async Task<string?> UnsubscribeOnDevice(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (server.Current is not { } origin || await Read(origin) is not { } saved)
            {
                return null;
            }

            storage.Remove(Key(origin));
            AndroidNotifications.Clear();
            UnifiedPushTransport.Send(saved, UnifiedPushTransport.Unregister);
            pending?.TrySetResult(null);
            pending = null;
            return saved.Endpoint;
        }
        finally
        {
            gate.Release();
        }
    }

    #endregion

    #region Private Methods

    private async Task<TaskCompletionSource<PushDeviceRequest?>> Begin(
        Uri origin, string distributor, Guid user, string publicKey, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var saved = AndroidPushSubscription.Create(distributor, user);
            await storage.SetAsync(Key(origin), saved.Store());
            var source = new TaskCompletionSource<PushDeviceRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = source;
            UnifiedPushTransport.Send(saved, UnifiedPushTransport.Register, publicKey);
            return source;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ReceiveRegistration(
        Uri origin, AndroidPushSubscription saved, Intent intent, CancellationToken cancellationToken)
    {
        switch (intent.Action)
        {
            case UnifiedPushTransport.NewEndpoint:
                if (intent.GetStringExtra("endpoint") is { Length: <= 1000 } endpoint
                    && !endpoint.Contains('\n') && Uri.TryCreate(endpoint, UriKind.Absolute, out var address)
                    && address.Scheme == Uri.UriSchemeHttps && address.UserInfo.Length == 0 && address.Fragment.Length == 0)
                {
                    await SaveEndpoint(origin, saved, endpoint, cancellationToken);
                    UnifiedPushTransport.Acknowledge(saved, intent);
                }
                break;
            case UnifiedPushTransport.Unregistered:
            case UnifiedPushTransport.RegistrationFailed:
                storage.Remove(Key(origin));
                AndroidNotifications.Clear();
                pending?.TrySetResult(null);
                pending = null;
                await TellServerToStop(origin, saved, cancellationToken);
                break;
        }
    }

    private async Task SaveEndpoint(Uri origin, AndroidPushSubscription saved, string endpoint, CancellationToken cancellationToken)
    {
        var updated = saved with { Endpoint = endpoint };
        if (updated.Request() is not { } request)
        {
            return;
        }

        if (pending is { } waiting)
        {
            // The caller that asked for this registration tells the server, so there is nothing left to report.
            await storage.SetAsync(Key(origin), (updated with { Reported = endpoint }).Store());
            pending = null;
            waiting.TrySetResult(request);
            return;
        }

        if (saved.Endpoint != endpoint)
        {
            // Keep the new address first, so a message sent to it can be opened whether or not the server hears of it now.
            updated = updated with { Reported = saved.Reported ?? saved.Endpoint };
            await storage.SetAsync(Key(origin), updated.Store());
            await Report(origin, updated, cancellationToken);
        }
    }

    // Tells the server the address the distributor gave, then stops it using the one it held. A failure leaves what
    // the server was last told in place, so the next look tries again.
    private async Task<AndroidPushSubscription> Report(Uri origin, AndroidPushSubscription saved, CancellationToken cancellationToken)
    {
        if (saved.Request() is not { } request)
        {
            return saved;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var api = scope.ServiceProvider.GetRequiredService<DroverApi>();
            var made = await OwnerBoundWrite.Run(
                () => server.Current,
                origin,
                async () => (await api.GetSession(cancellationToken))?.UserId,
                saved.UserId,
                () => api.TurnOnPush(request, cancellationToken),
                () => api.TurnOffPush(request.Endpoint, CancellationToken.None),
                logger);
            if (!made)
            {
                return saved;
            }

            var reported = saved with { Reported = saved.Endpoint };
            await storage.SetAsync(Key(origin), reported.Store());
            if (saved.Reported is { } previous && previous != saved.Endpoint)
            {
                // The old address goes only while the same person is still signed in. If not, it stays and the
                // next look removes it, since the server was told the new one.
                await OwnerBoundWrite.Run(
                    () => server.Current,
                    origin,
                    async () => (await api.GetSession(cancellationToken))?.UserId,
                    saved.UserId,
                    () => api.TurnOffPush(previous, cancellationToken),
                    () => Task.CompletedTask);
            }

            return reported;
        }
        catch (Exception exception) when (exception is HttpRequestException or DroverApiException or OperationCanceledException)
        {
            logger.LogWarning("Drover could not tell the server the notification address changed; it will try again");
            return saved;
        }
    }

    // The registration is gone from the device, so the server must not keep sending to it. If the server cannot be
    // reached now it learns from the distributor, whose answer to a message for a gone registration it acts on.
    private async Task TellServerToStop(Uri origin, AndroidPushSubscription saved, CancellationToken cancellationToken)
    {
        string?[] addresses = [saved.Endpoint, saved.Reported];
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var api = scope.ServiceProvider.GetRequiredService<DroverApi>();
            foreach (var address in addresses.OfType<string>().Distinct())
            {
                // Each removal is checked on its own, so one made after the person signed out is not made at all.
                await OwnerBoundWrite.Run(
                    () => server.Current,
                    origin,
                    async () => (await api.GetSession(cancellationToken))?.UserId,
                    saved.UserId,
                    () => api.TurnOffPush(address, cancellationToken),
                    () => Task.CompletedTask);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or DroverApiException or OperationCanceledException)
        {
            logger.LogWarning("Drover could not tell the server that this device stopped receiving notifications");
        }
    }

    private async Task<AndroidPushSubscription?> Read(Uri? origin)
    {
        try
        {
            return origin is null ? null : AndroidPushSubscription.Read(await storage.GetAsync(Key(origin)));
        }
        catch (Exception)
        {
            logger.LogWarning("The platform could not read the Drover notification registration");
            return null;
        }
    }

    private static string Key(Uri origin) => $"drover.push.subscription:{origin.AbsoluteUri}";

    private static async Task<bool> AllowNotifications()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return AndroidNotifications.Allowed;
        }

        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            Preferences.Default.Set(PermissionAsked, true);
            return await Permissions.RequestAsync<Permissions.PostNotifications>() is PermissionStatus.Granted;
        });
    }

    #endregion
}
