using GameSaveHub.Core;
using GameSaveHub.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameSaveHub.Server.Api;

public sealed partial class SessionWatchdog(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<SessionWatchdog> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GameSaveHubDbContext>();
                var threshold = timeProvider.GetUtcNow().AddSeconds(-90);
                var sessions = await db.Sessions
                    .Where(x => x.ReleasedAtUtc == null && x.LastHeartbeatAtUtc < threshold && x.State != WorldSessionState.Interrupted)
                    .ToListAsync(stoppingToken);
                foreach (var session in sessions)
                {
                    session.State = WorldSessionState.Interrupted;
                }

                if (sessions.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    LogInterrupted(logger, sessions.Count);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogWatchdogFailure(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} session(s) marquée(s) Interrupted; les verrous restent actifs.")]
    private static partial void LogInterrupted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec du watchdog de sessions.")]
    private static partial void LogWatchdogFailure(ILogger logger, Exception exception);
}
