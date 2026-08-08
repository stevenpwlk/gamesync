using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Adapters.PlanetCrafter.GamePass;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Client.Service;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"), optional: true, reloadOnChange: false);
builder.Services.AddWindowsService(options => options.ServiceName = "GameSave Hub Client");
builder.Services.Configure<ClientServiceOptions>(builder.Configuration.GetSection(ClientServiceOptions.SectionName));
builder.Services.AddSingleton<ClientStateStore>();
builder.Services.AddSingleton<DeviceIdentity>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<RegisteredUserProfileResolver>();
builder.Services.AddSingleton<IGameSaveAdapter>(services =>
{
    var localAppData = services.GetRequiredService<RegisteredUserProfileResolver>().ResolveLocalApplicationData();
    return new PlanetCrafterGamePassAdapter(new PlanetCrafterGamePassOptions { LocalApplicationDataOverride = localAppData });
});
builder.Services.AddSingleton<ITransferSessionStore>(services =>
{
    var options = services.GetRequiredService<IOptions<ClientServiceOptions>>().Value;
    return new FileTransferSessionStore(options.TransferRootPath);
});
builder.Services.AddHttpClient<ServerEnrollmentClient>();
builder.Services.AddHttpClient<ITransferServerClient, AuthenticatedTransferServerClient>();
builder.Services.AddSingleton<TransferOrchestrator>();
builder.Services.AddHostedService<TransferRecoveryWorker>();
builder.Services.AddHostedService<TransferHeartbeatWorker>();
builder.Services.AddHostedService<PipeServerWorker>();

await builder.Build().RunAsync();
