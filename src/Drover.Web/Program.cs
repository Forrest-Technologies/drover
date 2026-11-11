var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<Root>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
