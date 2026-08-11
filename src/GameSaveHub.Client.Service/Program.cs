using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Adapters.PlanetCrafter.GamePass;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Client.Service;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
// La configuration par machine vit dans %ProgramData%\GameSaveHub, jamais dans le dossier
// d'installation : celui-ci est renommé en bloc à chaque mise à jour (Client -> Client.old,
// Client.new -> Client) et emporterait le fichier avec lui, laissant le service sans
// RegisteredUserSid — donc en échec au démarrage. %ProgramData%\GameSaveHub n'est jamais
// touché par la bascule, comme managed-slot.json et client-state.json qui y vivent déjà.
// L'ancien emplacement reste lu en premier, donc surchargé par le nouveau, le temps qu'un
// poste installé avant le Lot 3 soit migré (la migration est faite par l'updater).
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"), optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile(
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "GameSaveHub",
        "appsettings.local.json"),
    optional: true,
    reloadOnChange: false);
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
builder.Services.AddSingleton<IManagedSlotStore>(services =>
{
    var options = services.GetRequiredService<IOptions<ClientServiceOptions>>().Value;
    return new FileManagedSlotStore(options.ManagedSlotStatePath);
});
builder.Services.AddHttpClient<ServerEnrollmentClient>();
builder.Services.AddHttpClient<ITransferServerClient, AuthenticatedTransferServerClient>();
builder.Services.AddSingleton<TransferOrchestrator>();
builder.Services.AddSingleton<TransferTransitionGate>();
builder.Services.AddSingleton<GameLifecycleMonitor>();
builder.Services.AddSingleton<ManagedSlotCoordinator>();
builder.Services.AddHostedService<TransferRecoveryWorker>();
builder.Services.AddHostedService<TransferHeartbeatWorker>();
builder.Services.AddHostedService<GameLifecycleWorker>();
builder.Services.AddHostedService<PipeServerWorker>();

await builder.Build().RunAsync();
