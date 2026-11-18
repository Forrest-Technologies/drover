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
        var saved = await Read(server.Current);
        var requiresPermission = OperatingSystem.IsAndroidVersionAtLeast(33) && !Preferences.Default.Get(PermissionAsked, false);
        var support = !AndroidNotifications.Allowed && !requiresPermission
            ? DevicePushSupport.Blocked
            : UnifiedPushTransport.Distributors().Count == 0 ? DevicePushSupport.NeedsDistributor : DevicePushSupport.Available;
        return new(support, saved?.Endpoint);
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
            await storage.SetAsync(Key(origin), updated.Store());
            pending = null;
            waiting.TrySetResult(request);
            return;
        }

        if (saved.Endpoint != endpoint)
        {
            await using var scope = scopes.CreateAsyncScope();
            var api = scope.ServiceProvider.GetRequiredService<DroverApi>();
            if ((await api.GetSession(cancellationToken))?.UserId != saved.UserId)
            {
                return;
            }

            await api.TurnOnPush(request, cancellationToken);
            await storage.SetAsync(Key(origin), updated.Store());
            if (saved.Endpoint is { } previous)
            {
                await api.TurnOffPush(previous, cancellationToken);
            }
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
