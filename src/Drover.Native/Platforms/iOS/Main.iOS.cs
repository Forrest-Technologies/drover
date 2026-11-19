var host = UnoPlatformHostBuilder.Create()
    .App(() => new Drover.Native.App())
    .UseAppleUIKit()
    .Build();

host.Run();
