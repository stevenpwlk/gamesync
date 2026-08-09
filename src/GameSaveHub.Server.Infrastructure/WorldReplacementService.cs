using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GameSaveHub.Server.Infrastructure;

public sealed record WorldReplacementRequest(
    Guid WorldId,
    string ArtifactPath,
    Guid ExpectedCurrentVersionId,
    string SourcePlayerName,
    IReadOnlyList<string> RequiredPlayerNames,
    string Reason);

public sealed record WorldReplacementResult(
    Guid PreviousVersionId,
    Guid NewVersionId,
    string Sha256,
    long Length,
    bool ReusedVersion);

public sealed record WorldRestoreRequest(
    Guid WorldId,
    Guid VersionId,
    string Reason);

public sealed class WorldReplacementService(
    GameSaveHubDbContext db,
    ImmutableArtifactStore store,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorldReplacementResult> ReplaceAsync(
        WorldReplacementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 8)
            throw new InvalidOperationException("Une justification d'au moins 8 caractères est requise.");
        if (string.IsNullOrWhiteSpace(request.SourcePlayerName))
            throw new InvalidOperationException("Le pseudo du joueur source est requis.");

        await using var staged = await store.StageLocalAsync(request.ArtifactPath, cancellationToken);
        var requiredPlayers = request.RequiredPlayerNames
            .Append(request.SourcePlayerName)
            .ToArray();
        ArtifactTopologyValidator.Validate(staged.Summary, requiredPlayers);
        var sourcePlayer = staged.Summary.Players.Single(player =>
            Normalize(player.Name).Equals(Normalize(request.SourcePlayerName), StringComparison.OrdinalIgnoreCase));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var world = await db.Worlds.SingleOrDefaultAsync(
            candidate => candidate.Id == request.WorldId,
            cancellationToken)
            ?? throw new InvalidOperationException("Monde introuvable.");
        if (world.CurrentVersionId != request.ExpectedCurrentVersionId)
            throw new InvalidOperationException("La version courante ne correspond plus à la version attendue.");
        if (await db.Sessions.AnyAsync(
                session => session.WorldId == request.WorldId && session.ReleasedAtUtc == null,
                cancellationToken))
            throw new InvalidOperationException("Le monde possède une session active.");

        var previous = await db.SaveVersions.SingleOrDefaultAsync(
            version => version.Id == request.ExpectedCurrentVersionId && version.WorldId == request.WorldId,
            cancellationToken)
            ?? throw new InvalidOperationException("La version courante attendue est introuvable.");
        if (previous.Sha256.Equals(staged.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cet artefact est déjà la version courante.");

        var imported = await store.PublishStagedAsync(staged, cancellationToken);
        var replacement = await db.SaveVersions
            .Where(version => version.WorldId == request.WorldId && version.Sha256 == imported.Sha256)
            .OrderByDescending(version => version.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var reused = replacement is not null;
        if (replacement is null)
        {
            replacement = new SaveVersionEntity
            {
                Id = Guid.NewGuid(),
                WorldId = request.WorldId,
                SessionId = null,
                Sha256 = imported.Sha256,
                Length = imported.Length,
                StorageKey = imported.Sha256,
                CreatedAtUtc = timeProvider.GetUtcNow(),
                PublishedByPlayerName = sourcePlayer.Name
            };
            db.SaveVersions.Add(replacement);
        }

        if (!previous.IsProtected)
        {
            previous.IsProtected = true;
            previous.ProtectionReason = $"Remplacement administratif : {reason}"[..Math.Min(reason.Length + 29, 1024)];
        }

        world.CurrentVersionId = replacement.Id;
        db.AdminAudit.Add(new AdminAuditEntity
        {
            Id = Guid.NewGuid(),
            Action = "world.replace",
            TargetId = world.Id.ToString("D"),
            Reason = reason[..Math.Min(reason.Length, 1024)],
            DetailsJson = JsonSerializer.Serialize(new
            {
                previousVersionId = previous.Id,
                newVersionId = replacement.Id,
                sha256 = imported.Sha256,
                sourcePlayer = sourcePlayer.Name,
                reusedVersion = reused
            }, JsonOptions),
            PerformedAtUtc = timeProvider.GetUtcNow()
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WorldReplacementResult(previous.Id, replacement.Id, imported.Sha256, imported.Length, reused);
    }

    public async Task RestoreAsync(
        WorldRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 8)
            throw new InvalidOperationException("Une justification d'au moins 8 caractères est requise.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var world = await db.Worlds.SingleOrDefaultAsync(
            candidate => candidate.Id == request.WorldId,
            cancellationToken)
            ?? throw new InvalidOperationException("Monde introuvable.");
        if (await db.Sessions.AnyAsync(
                session => session.WorldId == request.WorldId && session.ReleasedAtUtc == null,
                cancellationToken))
            throw new InvalidOperationException("Le monde possède une session active.");
        var restoreVersion = await db.SaveVersions.SingleOrDefaultAsync(
            version => version.Id == request.VersionId && version.WorldId == request.WorldId,
            cancellationToken)
            ?? throw new InvalidOperationException("Version introuvable pour ce monde.");
        if (world.CurrentVersionId == restoreVersion.Id)
            throw new InvalidOperationException("Cette version est déjà courante.");

        _ = await store.ImportLocalAsync(store.GetObjectPath(restoreVersion.Sha256), cancellationToken);
        var previousVersionId = world.CurrentVersionId;
        world.CurrentVersionId = restoreVersion.Id;
        db.AdminAudit.Add(new AdminAuditEntity
        {
            Id = Guid.NewGuid(),
            Action = "world.restore",
            TargetId = world.Id.ToString("D"),
            Reason = reason[..Math.Min(reason.Length, 1024)],
            DetailsJson = JsonSerializer.Serialize(new
            {
                previousVersionId,
                restoredVersionId = restoreVersion.Id
            }, JsonOptions),
            PerformedAtUtc = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormC);
}
