using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameSaveHub.Core;
using GameSaveHub.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("GSH_CONNECTION_STRING")
    ?? "Data Source=data/gamesavehub.db;Cache=Shared;Pooling=True";
var options = new DbContextOptionsBuilder<GameSaveHubDbContext>().UseSqlite(connectionString).Options;
await using var db = new GameSaveHubDbContext(options);

if (args.Length < 2)
{
    Usage();
    return 2;
}

switch ($"{args[0].ToLowerInvariant()} {args[1].ToLowerInvariant()}")
{
    case "database migrate":
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        Console.WriteLine("Migrations appliquées; SQLite est en mode WAL.");
        return 0;

    case "database pending":
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        Console.WriteLine(pending.Length == 0 ? "Aucune migration en attente." : string.Join(Environment.NewLine, pending));
        return pending.Length == 0 ? 0 : 1;

    case "enrollment create":
        var minutes = args.Length >= 3 && int.TryParse(args[2], out var parsedMinutes) ? parsedMinutes : 60;
        if (minutes is < 1 or > 1440) throw new InvalidOperationException("Durée autorisée : 1 à 1440 minutes.");
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        db.Enrollments.Add(new EnrollmentEntity
        {
            Id = Guid.NewGuid(),
            CodeHash = Sha256(code),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(minutes)
        });
        await db.SaveChangesAsync();
        Console.WriteLine(code);
        Console.Error.WriteLine($"Invitation valable {minutes} minute(s). Elle ne sera plus affichée.");
        return 0;

    case "device list":
        foreach (var listedDevice in await db.Devices.OrderBy(x => x.Name).ToListAsync())
            Console.WriteLine($"{listedDevice.Id:D}\t{listedDevice.Name}\t{(listedDevice.RevokedAtUtc is null ? "actif" : "révoqué")}");
        return 0;

    case "device revoke":
        RequireArgCount(args, 3);
        var deviceId = Guid.Parse(args[2]);
        var deviceToRevoke = await db.Devices.FindAsync(deviceId) ?? throw new InvalidOperationException("Appareil introuvable.");
        deviceToRevoke.RevokedAtUtc = DateTimeOffset.UtcNow;
        Audit(db, "device.revoke", deviceId.ToString("D"), "Révocation demandée par l'administrateur local.");
        await db.SaveChangesAsync();
        Console.WriteLine($"Appareil {deviceId:D} révoqué.");
        return 0;

    case "world create":
        RequireArgCount(args, 3);
        var world = new WorldEntity { Id = Guid.NewGuid(), Name = args[2].Trim(), CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Worlds.Add(world);
        await db.SaveChangesAsync();
        Console.WriteLine($"{world.Id:D}\t{world.Name}");
        return 0;

    case "world list":
        foreach (var listedWorld in await db.Worlds.OrderBy(x => x.Name).ToListAsync())
            Console.WriteLine($"{listedWorld.Id:D}\t{listedWorld.Name}\t{listedWorld.CurrentVersionId?.ToString("D") ?? "aucune version"}");
        return 0;

    case "world import":
        RequireArgCount(args, 4);
        var importWorldId = Guid.Parse(args[2]);
        var importWorld = await db.Worlds.FindAsync(importWorldId) ?? throw new InvalidOperationException("Monde introuvable.");
        if (importWorld.CurrentVersionId is not null) throw new InvalidOperationException("Le monde possède déjà une version. Utilisez world restore pour revenir à une version connue.");
        if (await db.Sessions.AnyAsync(x => x.WorldId == importWorldId && x.ReleasedAtUtc == null)) throw new InvalidOperationException("Monde verrouillé.");
        var importPath = Path.GetFullPath(args[3]);
        var maximumBytes = ReadMaximumArtifactBytes();
        await ArtifactEnvelopeValidator.ValidateAsync(importPath, maximumBytes);
        var importHash = await FileSafety.ComputeSha256Async(importPath);
        var importLength = new FileInfo(importPath).Length;
        var storageRoot = ReadStorageRoot();
        var objectPath = Path.Combine(storageRoot, "objects", importHash[..2], importHash[2..4], importHash + ".gshsave");
        await PublishObjectAsync(importPath, objectPath, importHash);
        var initialVersion = new SaveVersionEntity
        {
            Id = Guid.NewGuid(),
            WorldId = importWorld.Id,
            SessionId = null,
            Sha256 = importHash,
            Length = importLength,
            StorageKey = importHash,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.SaveVersions.Add(initialVersion);
        importWorld.CurrentVersionId = initialVersion.Id;
        Audit(db, "world.import", importWorld.Id.ToString("D"), "Import initial validé localement.", new { versionId = initialVersion.Id, sha256 = importHash, length = importLength });
        await db.SaveChangesAsync();
        Console.WriteLine($"{initialVersion.Id:D}\t{importHash}\t{importLength}");
        return 0;

    case "world restore":
        RequireArgCount(args, 5);
        var restoreWorldId = Guid.Parse(args[2]);
        var restoreVersionId = Guid.Parse(args[3]);
        var restoreReason = string.Join(' ', args.Skip(4)).Trim();
        if (restoreReason.Length < 8) throw new InvalidOperationException("Une justification d'au moins 8 caractères est requise.");
        var restoreWorld = await db.Worlds.FindAsync(restoreWorldId) ?? throw new InvalidOperationException("Monde introuvable.");
        var restoreVersion = await db.SaveVersions.SingleOrDefaultAsync(x => x.Id == restoreVersionId && x.WorldId == restoreWorldId)
            ?? throw new InvalidOperationException("Version introuvable pour ce monde.");
        if (await db.Sessions.AnyAsync(x => x.WorldId == restoreWorldId && x.ReleasedAtUtc == null)) throw new InvalidOperationException("Monde verrouillé.");
        var restoreObjectPath = Path.Combine(ReadStorageRoot(), "objects", restoreVersion.Sha256[..2], restoreVersion.Sha256[2..4], restoreVersion.Sha256 + ".gshsave");
        if (!File.Exists(restoreObjectPath) || await FileSafety.ComputeSha256Async(restoreObjectPath) != restoreVersion.Sha256)
            throw new InvalidOperationException("Objet immuable absent ou corrompu.");
        var previousVersion = restoreWorld.CurrentVersionId;
        restoreWorld.CurrentVersionId = restoreVersion.Id;
        Audit(db, "world.restore", restoreWorld.Id.ToString("D"), restoreReason, new { previousVersion, restoredVersion = restoreVersion.Id });
        await db.SaveChangesAsync();
        Console.WriteLine($"Monde {restoreWorld.Id:D} restauré vers {restoreVersion.Id:D}.");
        return 0;

    case "version list":
        RequireArgCount(args, 3);
        var versionWorldId = Guid.Parse(args[2]);
        foreach (var version in await db.SaveVersions.Where(x => x.WorldId == versionWorldId).OrderByDescending(x => x.CreatedAtUtc).ToListAsync())
            Console.WriteLine($"{version.Id:D}\t{version.CreatedAtUtc:O}\t{version.Length}\t{version.Sha256}");
        return 0;

    case "version protect":
        RequireArgCount(args, 4);
        var protectedVersionId = Guid.Parse(args[2]);
        var protectionReason = string.Join(' ', args.Skip(3)).Trim();
        if (protectionReason.Length < 8) throw new InvalidOperationException("Une justification d'au moins 8 caractères est requise.");
        var protectedVersion = await db.SaveVersions.FindAsync(protectedVersionId) ?? throw new InvalidOperationException("Version introuvable.");
        protectedVersion.IsProtected = true;
        protectedVersion.ProtectionReason = protectionReason[..Math.Min(protectionReason.Length, 1024)];
        Audit(db, "version.protect", protectedVersionId.ToString("D"), protectionReason);
        await db.SaveChangesAsync();
        Console.WriteLine($"Version {protectedVersionId:D} protégée de la rétention.");
        return 0;

    case "retention plan":
    case "retention purge":
        RequireArgCount(args, 3);
        var retentionWorldId = Guid.Parse(args[2]);
        var retentionWorld = await db.Worlds.FindAsync(retentionWorldId) ?? throw new InvalidOperationException("Monde introuvable.");
        if (await db.Sessions.AnyAsync(x => x.WorldId == retentionWorldId && x.ReleasedAtUtc == null)) throw new InvalidOperationException("Monde verrouillé.");
        var retentionVersions = await db.SaveVersions.Where(x => x.WorldId == retentionWorldId).ToListAsync();
        var latestCount = ReadRetentionCount("GSH_RETENTION_LATEST", 20);
        var dailyCount = ReadRetentionCount("GSH_RETENTION_DAILY", 30);
        var weeklyCount = ReadRetentionCount("GSH_RETENTION_WEEKLY", 26);
        var keepIds = RetentionPolicy.SelectVersionsToKeep(
            retentionVersions.Select(x => new VersionRetentionCandidate(x.Id, x.CreatedAtUtc, x.Id == retentionWorld.CurrentVersionId, x.IsProtected)),
            latestCount, dailyCount, weeklyCount);
        var purgeVersions = retentionVersions.Where(x => !keepIds.Contains(x.Id)).ToArray();
        foreach (var retentionVersion in retentionVersions.OrderByDescending(x => x.CreatedAtUtc))
            Console.WriteLine($"{(keepIds.Contains(retentionVersion.Id) ? "KEEP" : "PURGE")}\t{retentionVersion.Id:D}\t{retentionVersion.CreatedAtUtc:O}\t{retentionVersion.Sha256}");
        if (args[1].Equals("plan", StringComparison.OrdinalIgnoreCase)) return 0;
        db.SaveVersions.RemoveRange(purgeVersions);
        Audit(db, "retention.purge", retentionWorldId.ToString("D"), $"Politique {latestCount} dernières, {dailyCount} quotidiennes, {weeklyCount} hebdomadaires.", new { purged = purgeVersions.Select(x => x.Id) });
        await db.SaveChangesAsync();
        foreach (var sha256 in purgeVersions.Select(x => x.Sha256).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await db.SaveVersions.AnyAsync(x => x.Sha256 == sha256)) continue;
            var unreferencedPath = Path.Combine(ReadStorageRoot(), "objects", sha256[..2], sha256[2..4], sha256 + ".gshsave");
            if (File.Exists(unreferencedPath)) File.Delete(unreferencedPath);
        }
        Console.WriteLine($"{purgeVersions.Length} version(s) purgée(s). Les versions courante et protégées sont conservées.");
        return 0;

    case "storage verify":
        var broken = 0;
        foreach (var storedVersion in await db.SaveVersions.ToListAsync())
        {
            var storedPath = Path.Combine(ReadStorageRoot(), "objects", storedVersion.Sha256[..2], storedVersion.Sha256[2..4], storedVersion.Sha256 + ".gshsave");
            var valid = File.Exists(storedPath) && await FileSafety.ComputeSha256Async(storedPath) == storedVersion.Sha256;
            Console.WriteLine($"{storedVersion.Id:D}\t{(valid ? "OK" : "CORROMPU_OU_ABSENT")}");
            if (!valid) broken++;
        }
        return broken == 0 ? 0 : 1;

    case "session list":
        foreach (var session in await db.Sessions.OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync())
            Console.WriteLine($"{session.Id:D}\t{session.WorldId:D}\t{session.DeviceId:D}\t{session.State}\t{(session.ReleasedAtUtc is null ? "verrou actif" : "libéré")}");
        return 0;

    case "session release":
        RequireArgCount(args, 4);
        var sessionId = Guid.Parse(args[2]);
        var reason = string.Join(' ', args.Skip(3)).Trim();
        if (reason.Length < 8) throw new InvalidOperationException("Une justification d'au moins 8 caractères est requise.");
        var target = await db.Sessions.FindAsync(sessionId) ?? throw new InvalidOperationException("Session introuvable.");
        if (target.ReleasedAtUtc is not null) throw new InvalidOperationException("Session déjà libérée.");
        target.State = WorldSessionState.Failed;
        target.ReleasedAtUtc = DateTimeOffset.UtcNow;
        target.FailureCode = "admin_release";
        target.FailureMessage = reason[..Math.Min(reason.Length, 1024)];
        Audit(db, "session.release", sessionId.ToString("D"), reason);
        await db.SaveChangesAsync();
        Console.WriteLine($"Session {sessionId:D} libérée administrativement. Justification enregistrée.");
        return 0;

    default:
        Usage();
        return 2;
}

static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static void RequireArgCount(string[] values, int minimum)
{
    if (values.Length < minimum) throw new InvalidOperationException("Argument manquant.");
}

static void Usage()
{
    Console.Error.WriteLine("""
        GameSave Hub Admin
          database migrate|pending
          enrollment create [durée-minutes]
          device list|revoke <device-id>
          world create <nom>|list|import <world-id> <fichier.gshsave>|restore <world-id> <version-id> <justification>
          version list <world-id>|protect <version-id> <justification>
          retention plan|purge <world-id>
          session list|release <session-id> <justification>
          storage verify
        """);
}

static void Audit(GameSaveHubDbContext db, string action, string targetId, string reason, object? details = null)
{
    db.AdminAudit.Add(new AdminAuditEntity
    {
        Id = Guid.NewGuid(),
        Action = action,
        TargetId = targetId,
        Reason = reason,
        DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
        PerformedAtUtc = DateTimeOffset.UtcNow
    });
}

static string ReadStorageRoot() => Path.GetFullPath(Environment.GetEnvironmentVariable("GSH_STORAGE_ROOT") ?? "data");

static long ReadMaximumArtifactBytes() => long.TryParse(Environment.GetEnvironmentVariable("GSH_MAX_ARTIFACT_BYTES"), out var value)
    ? value
    : 64L * 1024 * 1024;

static int ReadRetentionCount(string variable, int defaultValue)
{
    var value = Environment.GetEnvironmentVariable(variable);
    if (string.IsNullOrWhiteSpace(value)) return defaultValue;
    if (!int.TryParse(value, out var parsed) || parsed is < 0 or > 10000) throw new InvalidOperationException($"Valeur invalide : {variable}.");
    return parsed;
}

static async Task PublishObjectAsync(string source, string destination, string expectedHash)
{
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    if (File.Exists(destination))
    {
        if (await FileSafety.ComputeSha256Async(destination) != expectedHash) throw new InvalidOperationException("Collision ou objet existant corrompu.");
        return;
    }
    var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
    try
    {
        File.Copy(source, temporary, overwrite: false);
        if (await FileSafety.ComputeSha256Async(temporary) != expectedHash) throw new InvalidOperationException("Copie de l'objet corrompue.");
        File.Move(temporary, destination);
    }
    finally
    {
        if (File.Exists(temporary)) File.Delete(temporary);
    }
}
