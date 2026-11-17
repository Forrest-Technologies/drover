namespace Drover.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<DroverApplication>();
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddDroverUi();
        builder.Services.AddSingleton(new UiEnvironment(false));
        builder.Services.AddSingleton<ServerAddress>();
        builder.Services.AddSingleton<ISecureStorage>(SecureStorage.Default);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<NativeSession>();
        builder.Services.AddSingleton<AppRequests>();
        builder.Services.AddScoped<IServerChoice, AppServerChoice>();
        builder.Services.AddSingleton<DocumentDestination>();
        builder.Services.AddScoped<IFileDelivery, AndroidFileDelivery>();
        builder.Services.AddSingleton<IncomingNavigation>();
        builder.Services.AddSingleton<AndroidShares>();
        builder.Services.AddSingleton(new AppSignInReturn(MainActivity.SignInReturnScheme));
        builder.Services.AddSingleton<AndroidPushRegistration>();
        builder.Services.AddSingleton<IPushRegistration>(services => services.GetRequiredService<AndroidPushRegistration>());
        builder.Services.AddScoped<AndroidPushMessages>();
        builder.Services.AddScoped(services => new HttpClient(
            services.GetRequiredService<AppRequests>(), disposeHandler: false)
        {
            BaseAddress = AppRequests.Placeholder,
        });
        builder.Services.AddScoped<DroverApi>();
        return builder.Build();
    }
}
