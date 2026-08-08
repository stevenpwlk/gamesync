using GameSaveHub.Core;
using GameSaveHub.Server.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GameSaveHub.Server.Api;

public sealed partial class PublicationReconciler(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PublicationReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var discoveryScope = scopeFactory.CreateAsyncScope();
        var discoveryDb = discoveryScope.ServiceProvider.GetRequiredService<GameSaveHubDbContext>();
        var sessionIds = await discoveryDb.Sessions
            .Where(x => x.ReleasedAtUtc == null && x.State == WorldSessionState.Publishing)
            .Select(x => x.Id)
            .ToListAsync(stoppingToken);

        foreach (var sessionId in sessionIds)
        {
            try
            {
                await ReconcileAsync(sessionId, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReconcileFailure(logger, sessionId, exception);
                await MarkInterruptedAsync(sessionId, "reconcile_failed", exception.GetType().Name, stoppingToken);
            }
        }
    }

    private async Task ReconcileAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameSaveHubDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<ImmutableArtifactStore>();
        var session = await db.Sessions.FindAsync([sessionId], cancellationToken);
        if (session is null || session.ReleasedAtUtc is not null || session.State != WorldSessionState.Publishing) return;
        var upload = await db.Uploads
            .Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Upload de publication absent.");

        if (upload.ResultVersionId is Guid completedVersion)
        {
            session.State = WorldSessionState.Completed;
            session.ReleasedAtUtc = upload.CommittedAtUtc ?? timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            LogRecovered(logger, sessionId, completedVersion);
            return;
        }

        var objectPath = store.GetObjectPath(upload.Sha256);
        if (!File.Exists(objectPath)) await store.AssembleAndPublishAsync(upload, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
        db.Database.UseTransaction(transaction);
        var world = await db.Worlds.FindAsync([session.WorldId], cancellationToken) ?? throw new InvalidOperationException("Monde absent.");
        if (world.CurrentVersionId != upload.BaseVersionId)
        {
            session.State = WorldSessionState.Interrupted;
            session.FailureCode = "base_version_mismatch";
            session.FailureMessage = "Reconciler: la version courante ne correspond plus à la base de l'upload.";
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var version = new SaveVersionEntity
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            SessionId = session.Id,
            Sha256 = upload.Sha256,
            Length = upload.Length,
            StorageKey = upload.Sha256,
            CreatedAtUtc = now
        };
        db.SaveVersions.Add(version);
        world.CurrentVersionId = version.Id;
        upload.ResultVersionId = version.Id;
        upload.CommittedAtUtc = now;
        session.State = WorldSessionState.Completed;
        session.ReleasedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogRecovered(logger, sessionId, version.Id);
    }

    private async Task MarkInterruptedAsync(Guid sessionId, string code, string message, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameSaveHubDbContext>();
        var session = await db.Sessions.FindAsync([sessionId], cancellationToken);
        if (session is null || session.ReleasedAtUtc is not null) return;
        session.State = WorldSessionState.Interrupted;
        session.FailureCode = code;
        session.FailureMessage = message;
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Publication de la session {SessionId} récupérée vers la version {VersionId}.")]
    private static partial void LogRecovered(ILogger logger, Guid sessionId, Guid versionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de récupération de la publication {SessionId}.")]
    private static partial void LogReconcileFailure(ILogger logger, Guid sessionId, Exception exception);
}
