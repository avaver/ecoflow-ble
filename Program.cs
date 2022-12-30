using ecoflow_ble;

IHost host = Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .ConfigureServices(services => 
    {
        services.AddSingleton<Bluetooth>();
        services.AddSingleton<MQTTClient>();
        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
