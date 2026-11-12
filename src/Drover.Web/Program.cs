var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<Root>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddDroverUi();
builder.Services.AddSingleton(new UiEnvironment(builder.HostEnvironment.IsDevelopment()));
builder.Services.AddScoped<AntiforgeryHandler>();
builder.Services.AddScoped(services => new DroverApi(new HttpClient(BrowserRequests(services))
{
    BaseAddress = new(builder.HostEnvironment.BaseAddress),
}));

await builder.Build().RunAsync();

static AntiforgeryHandler BrowserRequests(IServiceProvider services)
{
    var handler = services.GetRequiredService<AntiforgeryHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return handler;
}
