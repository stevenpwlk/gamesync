using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml.Linq;
using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Contracts;
using GameSaveHub.Core;
using Microsoft.Win32;

namespace GameSaveHub.Adapters.PlanetCrafter.GamePass;

public sealed class PlanetCrafterGamePassAdapter(PlanetCrafterGamePassOptions? options = null) : IGameSaveAdapter
{
    private const string AdapterId = "planet-crafter-pc-gamepass";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly PlanetCrafterGamePassOptions _options = options ?? new();

    public string Id => AdapterId;

    public AdapterCapabilityReport Capabilities => new(
        CanInspect: true,
        CanCreateSafetySnapshot: true,
        CanExportPortableArtifact: true,
        CanPrepareForHost: true,
        CanImportPortableArtifact: true,
        CanLaunchGame: true,
        GateStatus: "pilot-validated-production-gate-required");

    public Task<InstallationDetection> DetectInstallationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packageRoot = GetPackageRoot();
        var wgsRoot = Path.Combine(packageRoot, "SystemAppData", "wgs");
        var warnings = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            warnings.Add("L'adaptateur Game Pass est pris en charge uniquement sous Windows.");
        }
        if (!Directory.Exists(wgsRoot))
        {
            warnings.Add("Le dossier WGS n'existe pas encore. Lancez le jeu une fois avec le bon compte Xbox.");
        }

        var appx = FindAppxRegistration();
        if (Directory.Exists(packageRoot) && appx.PackageFullName is null && _options.LocalApplicationDataOverride is null)
        {
            warnings.Add("Le profil local existe, mais l'enregistrement AppX n'a pas pu être lu.");
        }

        return Task.FromResult(new InstallationDetection(
            Directory.Exists(packageRoot),
            _options.PackageFamilyName,
            appx.PackageFullName,
            appx.Version,
            appx.InstallLocation,
            Directory.Exists(packageRoot) ? packageRoot : null,
            Directory.Exists(wgsRoot) ? wgsRoot : null,
            warnings));
    }

    public async Task<LocalStorageInspection> InspectLocalStorageAsync(CancellationToken cancellationToken = default)
    {
        var detection = await DetectInstallationAsync(cancellationToken);
        if (!detection.IsInstalled || detection.WgsRoot is null)
        {
            throw new InvalidOperationException("Installation Planet Crafter Game Pass ou stockage WGS introuvable.");
        }

        var processes = ProbeProcesses();
        var warnings = detection.Warnings.ToList();
        if (processes.Count > 0)
        {
            warnings.Add("Le jeu est en cours d'exécution : cet inventaire est en lecture seule et peut changer pendant la capture.");
        }

        var files = new List<DiagnosticFile>();
        foreach (var path in EnumerateSafeFiles(detection.WgsRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var infoBefore = new FileInfo(path);
            FileSafety.RejectReparsePoint(infoBefore);
            var lengthBefore = infoBefore.Length;
            var writeBefore = infoBefore.LastWriteTimeUtc;
            var hash = await FileSafety.ComputeSha256Async(path, cancellationToken);
            infoBefore.Refresh();
            var stable = lengthBefore == infoBefore.Length && writeBefore == infoBefore.LastWriteTimeUtc;
            var relative = FileSafety.GetSafeRelativePath(detection.WgsRoot, path);
            files.Add(new DiagnosticFile(relative, infoBefore.Length, infoBefore.LastWriteTimeUtc, hash, Classify(path), stable));
        }

        if (files.Any(file => !file.StableDuringRead))
        {
            warnings.Add("Au moins un fichier a changé pendant sa lecture; la capture n'est pas cohérente.");
        }

        var (worlds, discoveryWarnings) = await DiscoverWorldsAsync(detection.WgsRoot, cancellationToken);
        warnings.AddRange(discoveryWarnings);

        return new LocalStorageInspection(
            1,
            AdapterId,
            _options.PackageFamilyName,
            DateTimeOffset.UtcNow,
            processes.Count > 0,
            processes.Count == 0 && files.All(file => file.StableDuringRead),
            processes.Select(process => $"{process.Name} ({process.Id})").ToArray(),
            files,
            worlds,
            warnings);
    }

    public async Task<SnapshotResult> CreateSafetySnapshotAsync(
        string outputRoot,
        string? acknowledgedTestWorldName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(acknowledgedTestWorldName))
        {
            return Failed("La capture est refusée sans cible de monde test explicitement reconnue.");
        }

        var detection = await DetectInstallationAsync(cancellationToken);
        if (detection.WgsRoot is null)
        {
            return Failed("Stockage WGS introuvable.");
        }
        if (ProbeProcesses().Count > 0)
        {
            return Failed("Fermez complètement The Planet Crafter avant de créer une capture.");
        }

        var fullOutputRoot = Path.GetFullPath(outputRoot);
        if (FileSafety.IsSameOrDescendant(fullOutputRoot, detection.WgsRoot) || FileSafety.IsSameOrDescendant(detection.WgsRoot, fullOutputRoot))
        {
            return Failed("Le dossier de sortie doit être totalement séparé du stockage WGS.");
        }

        var before = await InspectLocalStorageAsync(cancellationToken);
        if (!before.Stable || before.GameRunning)
        {
            return Failed("Le stockage n'est pas stable ou le jeu est encore ouvert.");
        }
        if (!before.Worlds.Any(world => DisplayNamesEquivalent(world.DisplayName, acknowledgedTestWorldName)))
        {
            return Failed($"Le monde test déclaré '{acknowledgedTestWorldName}' n'a pas été trouvé dans les métadonnées WGS.");
        }

        Directory.CreateDirectory(fullOutputRoot);
        var snapshotId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
        var temporary = Path.Combine(fullOutputRoot, $".{snapshotId}.partial");
        var destination = Path.Combine(fullOutputRoot, snapshotId);

        try
        {
            Directory.CreateDirectory(temporary);
            foreach (var file in before.Files)
            {
                var source = Path.Combine(detection.WgsRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var target = Path.Combine(temporary, "wgs", file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: false);
                var copiedHash = await FileSafety.ComputeSha256Async(target, cancellationToken);
                if (!string.Equals(copiedHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"Hash différent après copie : {file.RelativePath}");
                }
            }

            if (ProbeProcesses().Count > 0)
            {
                throw new IOException("Le jeu a été lancé pendant la capture.");
            }

            var after = await InspectLocalStorageAsync(cancellationToken);
            if (!Equivalent(before.Files, after.Files))
            {
                throw new IOException("Le stockage WGS a changé pendant la capture.");
            }

            var manifest = new SafetySnapshotManifest(1, snapshotId, AdapterId, _options.PackageFamilyName, DateTimeOffset.UtcNow, acknowledgedTestWorldName, before.Files);
            var manifestPath = Path.Combine(temporary, "snapshot-manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
            Directory.Move(temporary, destination);
            return new SnapshotResult(true, destination, manifest, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
            if (exception is OperationCanceledException)
            {
                throw;
            }
            return Failed(exception.Message);
        }
    }

    public Task<GameProcessDetection> DetectGameProcessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processes = ProbeProcesses();
        return Task.FromResult(new GameProcessDetection(processes.Count > 0, processes.Select(process => process.Id).ToArray()));
    }

    public async Task<TestWorldRestoreResult> RestoreTestWorldFromSnapshotAsync(
        string sourceSnapshotDirectory,
        string testWorldName,
        string backupOutputRoot,
        bool offlineAcknowledged,
        CancellationToken cancellationToken = default)
    {
        if (!offlineAcknowledged)
        {
            return RestoreFailed("La restauration est refusée sans --acknowledge-offline.");
        }
        if (HasActiveNetworkRoute())
        {
            return RestoreFailed("Une route réseau active est détectée. Désactivez le Wi-Fi, Ethernet et les VPN avant l'essai.");
        }
        if (ProbeProcesses().Count > 0)
        {
            return RestoreFailed("Fermez complètement The Planet Crafter avant la restauration.");
        }

        var detection = await DetectInstallationAsync(cancellationToken);
        if (detection.WgsRoot is null) return RestoreFailed("Stockage WGS courant introuvable.");
        var current = await InspectLocalStorageAsync(cancellationToken);
        if (!current.Stable) return RestoreFailed("Le stockage WGS courant n'est pas stable.");
        var currentWorld = current.Worlds.SingleOrDefault(world => DisplayNamesEquivalent(world.DisplayName, testWorldName));
        if (currentWorld is null) return RestoreFailed($"Monde test courant introuvable : {testWorldName}.");

        var sourceRoot = Path.GetFullPath(sourceSnapshotDirectory);
        var sourceWgsRoot = Path.Combine(sourceRoot, "wgs");
        var manifestPath = Path.Combine(sourceRoot, "snapshot-manifest.json");
        var sourceValidation = await ValidateSnapshotSourceAsync(sourceRoot, manifestPath, cancellationToken);
        if (sourceValidation.Manifest is null) return RestoreFailed(sourceValidation.Errors);
        if (!DisplayNamesEquivalent(sourceValidation.Manifest.AcknowledgedTestWorldName, testWorldName))
        {
            return RestoreFailed("Le snapshot source n'a pas été créé pour ce monde test.");
        }

        var (sourceWorlds, sourceWarnings) = await DiscoverWorldsAsync(sourceWgsRoot, cancellationToken);
        var sourceWorld = sourceWorlds.SingleOrDefault(world => DisplayNamesEquivalent(world.DisplayName, testWorldName));
        if (sourceWorld is null) return RestoreFailed([.. sourceWarnings, $"Monde test absent du snapshot source : {testWorldName}."]);
        if (sourceWorld.WorldSeed != currentWorld.WorldSeed || !sourceWorld.LogicalName.Equals(currentWorld.LogicalName, StringComparison.OrdinalIgnoreCase))
        {
            return RestoreFailed("Le snapshot source ne correspond pas au même monde logique et seed.");
        }

        var preRestore = await CreateSafetySnapshotAsync(backupOutputRoot, testWorldName, cancellationToken);
        if (!preRestore.Success || preRestore.SnapshotDirectory is null)
        {
            return RestoreFailed(["Impossible de créer le snapshot automatique avant restauration.", .. preRestore.Errors]);
        }

        var sourceBlob = ResolveContainedPath(sourceWgsRoot, sourceWorld.BlobRelativePath);
        var targetBlob = ResolveContainedPath(detection.WgsRoot, currentWorld.BlobRelativePath);
        var previousHash = await FileSafety.ComputeSha256Async(targetBlob, cancellationToken);
        var restoredHash = await FileSafety.ComputeSha256Async(sourceBlob, cancellationToken);
        var temporary = Path.Combine(Path.GetDirectoryName(targetBlob)!, $".gsh-{Guid.NewGuid():N}.tmp");

        try
        {
            await CopyWithFlushAsync(sourceBlob, temporary, cancellationToken);
            if (!restoredHash.Equals(await FileSafety.ComputeSha256Async(temporary, cancellationToken), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le hash temporaire diffère du snapshot source.");
            }
            if (ProbeProcesses().Count > 0 || HasActiveNetworkRoute())
            {
                throw new IOException("Le jeu ou le réseau a été réactivé pendant la préparation; aucune écriture n'a été effectuée.");
            }

            File.Move(temporary, targetBlob, overwrite: true);
            var finalHash = await FileSafety.ComputeSha256Async(targetBlob, cancellationToken);
            if (!restoredHash.Equals(finalHash, StringComparison.OrdinalIgnoreCase))
            {
                await RollBackFromSnapshotAsync(preRestore.SnapshotDirectory, testWorldName, detection.WgsRoot, targetBlob, cancellationToken);
                return RestoreFailed("La vérification après remplacement a échoué; le blob précédent a été restauré.");
            }

            return new TestWorldRestoreResult(true, preRestore.SnapshotDirectory, currentWorld.LogicalName, previousHash, restoredHash, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            return RestoreFailed(exception.Message);
        }
    }

    public Task<DiagnosticSafetyStatus> GetDiagnosticSafetyStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gameRunning = ProbeProcesses().Count > 0;
        var activeNetworkRoute = HasActiveNetworkRoute();
        return Task.FromResult(new DiagnosticSafetyStatus(gameRunning, activeNetworkRoute, !gameRunning && !activeNetworkRoute));
    }

    public async Task<LogicalSnapshotDifference> CompareSnapshotsLogicallyAsync(
        string beforeSnapshotDirectory,
        string afterSnapshotDirectory,
        CancellationToken cancellationToken = default)
    {
        var beforeRoot = Path.GetFullPath(beforeSnapshotDirectory);
        var afterRoot = Path.GetFullPath(afterSnapshotDirectory);
        var beforeValidation = await ValidateSnapshotSourceAsync(beforeRoot, Path.Combine(beforeRoot, "snapshot-manifest.json"), cancellationToken);
        var afterValidation = await ValidateSnapshotSourceAsync(afterRoot, Path.Combine(afterRoot, "snapshot-manifest.json"), cancellationToken);
        if (beforeValidation.Manifest is null) throw new InvalidDataException($"Snapshot avant invalide : {string.Join("; ", beforeValidation.Errors)}");
        if (afterValidation.Manifest is null) throw new InvalidDataException($"Snapshot après invalide : {string.Join("; ", afterValidation.Errors)}");
        if (!beforeValidation.Manifest.AdapterId.Equals(Id, StringComparison.Ordinal) ||
            !afterValidation.Manifest.AdapterId.Equals(Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Les snapshots appartiennent à un autre adaptateur.");
        }

        var beforeFiles = await DiscoverLogicalFilesAsync(Path.Combine(beforeRoot, "wgs"), cancellationToken);
        var afterFiles = await DiscoverLogicalFilesAsync(Path.Combine(afterRoot, "wgs"), cancellationToken);
        var names = beforeFiles.Keys.Union(afterFiles.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var differences = new List<LogicalFileDifference>();
        foreach (var name in names)
        {
            beforeFiles.TryGetValue(name, out var before);
            afterFiles.TryGetValue(name, out var after);
            var status = before is null ? "Added" : after is null ? "Removed" : before.Sha256.Equals(after.Sha256, StringComparison.OrdinalIgnoreCase) ? "Unchanged" : "Changed";
            differences.Add(new LogicalFileDifference(name, status, before?.Sha256, after?.Sha256, before?.Length, after?.Length));
        }
        return new LogicalSnapshotDifference(differences);
    }

    public async Task<PortableSaveArtifact> ExportPortableArtifactAsync(
        string worldName,
        string outputRoot,
        CancellationToken cancellationToken = default) =>
        await ExportAsync(worldName, byLogicalName: false, outputRoot, cancellationToken);

    /// <summary>
    /// Exporte en désignant le monde par son nom logique.
    /// </summary>
    /// <remarks>
    /// Le nom affiché n'est pas unique : deux imports successifs de la même
    /// sauvegarde produisent deux mondes homonymes. Toute résolution par nom affiché
    /// devient alors ambiguë, alors que le nom logique reste discriminant.
    /// C'est la voie que doit emprunter l'orchestrateur, qui connaît sa cible exacte.
    /// </remarks>
    public async Task<PortableSaveArtifact> ExportPortableArtifactByLogicalNameAsync(
        string logicalName,
        string outputRoot,
        CancellationToken cancellationToken = default) =>
        await ExportAsync(logicalName, byLogicalName: true, outputRoot, cancellationToken);

    private async Task<PortableSaveArtifact> ExportAsync(
        string name,
        bool byLogicalName,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        if (ProbeProcesses().Count > 0) throw new InvalidOperationException("Fermez complètement The Planet Crafter avant l'export.");
        var detection = await DetectInstallationAsync(cancellationToken);
        if (detection.WgsRoot is null) throw new InvalidOperationException("Stockage WGS introuvable.");
        var fullOutputRoot = Path.GetFullPath(outputRoot);
        var finalPathResolver = _options.FinalPathResolver ?? FileSafety.ResolveDirectoryLinks;
        var resolvedOutputRoot = finalPathResolver(fullOutputRoot);
        var resolvedWgsRoot = finalPathResolver(detection.WgsRoot);
        if (FileSafety.IsNetworkPath(resolvedOutputRoot))
            throw new InvalidOperationException("Le dossier d'export doit se trouver sur un disque local.");
        if (FileSafety.IsSameOrDescendant(fullOutputRoot, detection.WgsRoot) ||
            FileSafety.IsSameOrDescendant(detection.WgsRoot, fullOutputRoot) ||
            FileSafety.IsSameOrDescendant(resolvedOutputRoot, resolvedWgsRoot) ||
            FileSafety.IsSameOrDescendant(resolvedWgsRoot, resolvedOutputRoot))
        {
            throw new InvalidOperationException("Le dossier d'export doit être séparé du stockage WGS.");
        }

        var before = await InspectLocalStorageAsync(cancellationToken);
        if (!before.Stable) throw new InvalidOperationException("Le stockage WGS n'est pas stable.");
        var matches = byLogicalName
            ? before.Worlds.Where(item => item.LogicalName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray()
            : before.Worlds.Where(item => item.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) throw new InvalidOperationException($"Monde introuvable : {name}.");
        if (matches.Length > 1)
        {
            // Diagnostic explicite plutot qu'une exception LINQ illisible : c'est la
            // situation normale des que la meme sauvegarde a ete importee deux fois.
            var logicalNames = string.Join(", ", matches.Select(item => item.LogicalName));
            throw new InvalidOperationException(
                $"Plusieurs mondes portent le nom « {name} » ({logicalNames}). Désignez-le par son nom logique.");
        }
        var world = matches[0];
        var source = ResolveContainedPath(detection.WgsRoot, world.BlobRelativePath);
        var payloadHash = await FileSafety.ComputeSha256Async(source, cancellationToken);
        var payloadLength = new FileInfo(source).Length;
        if (payloadLength > 256L * 1024 * 1024) throw new InvalidDataException("Le monde dépasse la limite de 256 Mio.");

        var manifest = new PortableArtifactManifest(
            1,
            AdapterId,
            DateTimeOffset.UtcNow,
            world.LogicalName,
            world.DisplayName,
            world.PlanetId,
            world.Mode,
            world.WorldSeed,
            "payload/world.save",
            payloadLength,
            payloadHash,
            world.Players);

        Directory.CreateDirectory(fullOutputRoot);
        var artifactName = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.gshsave";
        var destination = Path.Combine(fullOutputRoot, artifactName);
        var temporary = destination + ".partial";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.NoCompression);
                    await using (var manifestStream = manifestEntry.Open())
                    {
                        await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
                    }
                    var payloadEntry = archive.CreateEntry(manifest.PayloadPath, CompressionLevel.NoCompression);
                    await using var payloadStream = payloadEntry.Open();
                    await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await input.CopyToAsync(payloadStream, cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            if (ProbeProcesses().Count > 0) throw new IOException("Le jeu a été lancé pendant l'export.");
            var after = await InspectLocalStorageAsync(cancellationToken);
            // Relecture par nom logique : le nom affiche peut avoir change avec le
            // contenu, et il ne designe de toute facon pas un monde de maniere unique.
            var afterWorld = after.Worlds.SingleOrDefault(item => item.LogicalName.Equals(world.LogicalName, StringComparison.OrdinalIgnoreCase))
                ?? throw new IOException("Le monde exporté a disparu pendant l'export.");
            var afterSource = ResolveContainedPath(detection.WgsRoot, afterWorld.BlobRelativePath);
            if (!payloadHash.Equals(await FileSafety.ComputeSha256Async(afterSource, cancellationToken), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le monde a changé pendant l'export.");
            }

            File.Move(temporary, destination);
            var artifactHash = await FileSafety.ComputeSha256Async(destination, cancellationToken);
            var artifact = new PortableSaveArtifact(destination, artifactHash, new FileInfo(destination).Length, manifest);
            var validation = await ValidateArtifactAsync(artifact, cancellationToken);
            if (!validation.IsValid)
            {
                File.Delete(destination);
                throw new InvalidDataException($"Artefact exporté invalide : {string.Join("; ", validation.Errors)}");
            }
            return artifact;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    public async Task<ArtifactValidation> ValidateArtifactAsync(PortableSaveArtifact artifact, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (!File.Exists(artifact.Path)) return new ArtifactValidation(false, ["Fichier d'artefact absent."]);
        if (new FileInfo(artifact.Path).Length > 257L * 1024 * 1024) return new ArtifactValidation(false, ["Artefact trop volumineux."]);
        try
        {
            await using var stream = new FileStream(artifact.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            if (entries.Count != 2 ||
                !entries.TryGetValue("manifest.json", out var manifestEntry) ||
                !entries.TryGetValue("payload/world.save", out var payloadEntry))
            {
                return new ArtifactValidation(false, ["L'archive doit contenir exactement manifest.json et payload/world.save."]);
            }
            if (manifestEntry.Length is <= 0 or > 64 * 1024) errors.Add("Taille de manifeste invalide.");
            PortableArtifactManifest? manifest = null;
            if (errors.Count == 0)
            {
                await using var manifestStream = manifestEntry.Open();
                manifest = await JsonSerializer.DeserializeAsync<PortableArtifactManifest>(manifestStream, JsonOptions, cancellationToken);
            }
            if (manifest is null || manifest.SchemaVersion != 1 || manifest.AdapterId != AdapterId) errors.Add("Manifeste absent, incompatible ou non reconnu.");
            if (manifest is not null && manifest.PayloadPath != "payload/world.save") errors.Add("Chemin de payload non autorisé.");

            if (payloadEntry.Length is <= 0 or > 256L * 1024 * 1024) errors.Add("Taille de payload invalide.");
            if (payloadEntry.CompressedLength <= 0 || payloadEntry.Length > payloadEntry.CompressedLength * 10) errors.Add("Ratio de compression du payload refusé.");
            if (manifest is not null && payloadEntry.Length != manifest.PayloadLength) errors.Add("Longueur du payload différente du manifeste.");
            await using var payload = payloadEntry.Open();
            var hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(payload, cancellationToken));
            if (manifest is not null && !hash.Equals(manifest.PayloadSha256, StringComparison.OrdinalIgnoreCase)) errors.Add("Hash du payload invalide.");
            if (manifest is not null && errors.Count == 0)
            {
                await using var semanticPayload = payloadEntry.Open();
                using var reader = new StreamReader(semanticPayload, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
                var text = await reader.ReadToEndAsync(cancellationToken);
                var parsed = PlanetCrafterWorldPayloadReader.Parse(manifest.LogicalName, text, manifest.PayloadPath);
                if (parsed is null ||
                    !parsed.DisplayName.Equals(manifest.DisplayName, StringComparison.Ordinal) ||
                    parsed.WorldSeed != manifest.WorldSeed ||
                    !parsed.LogicalName.Equals(manifest.LogicalName, StringComparison.Ordinal) ||
                    !PlayersEquivalent(parsed.Players, manifest.Players))
                {
                    errors.Add("Le contenu sémantique du monde ne correspond pas au manifeste.");
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException or ArgumentException)
        {
            errors.Add($"Archive invalide : {exception.Message}");
        }
        return new ArtifactValidation(errors.Count == 0, errors);
    }
    public async Task<HostPreparation> PrepareForHostAsync(
        PortableSaveArtifact artifact,
        string playerName,
        string targetDisplayName,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateArtifactAsync(artifact, cancellationToken);
        if (!validation.IsValid)
        {
            return new HostPreparation(false, HostPreparationOutcome.InvalidArtifact, null, null, null, null, false, validation.Errors);
        }

        try
        {
            var (manifest, payload) = await ReadPortableArtifactAsync(artifact.Path, cancellationToken);
            var transform = PlanetCrafterWorldTransformer.PrepareForHost(payload, manifest.Players, playerName, targetDisplayName);
            if (!transform.Success || transform.Payload is null || transform.PreparedDisplayName is null || transform.PreparedPlayers is null)
            {
                return new HostPreparation(
                    false,
                    transform.Outcome,
                    null,
                    transform.TargetPlayerName,
                    transform.TargetPlayerOriginalId,
                    transform.PreviousHostPlayerId,
                    false,
                    transform.Errors);
            }

            var payloadHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(transform.Payload));
            var preparedManifest = manifest with
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                DisplayName = transform.PreparedDisplayName,
                PayloadLength = transform.Payload.LongLength,
                PayloadSha256 = payloadHash,
                Players = transform.PreparedPlayers
            };

            var preparedArtifact = await WritePortableArtifactAsync(
                preparedManifest,
                transform.Payload,
                outputRoot,
                "prepared-host",
                cancellationToken);
            var preparedValidation = await ValidateArtifactAsync(preparedArtifact, cancellationToken);
            if (!preparedValidation.IsValid)
            {
                if (File.Exists(preparedArtifact.Path)) File.Delete(preparedArtifact.Path);
                return new HostPreparation(false, HostPreparationOutcome.Failed, null, transform.TargetPlayerName,
                    transform.TargetPlayerOriginalId, transform.PreviousHostPlayerId, transform.Changed, preparedValidation.Errors);
            }

            return new HostPreparation(
                true,
                transform.Outcome,
                preparedArtifact,
                transform.TargetPlayerName,
                transform.TargetPlayerOriginalId,
                transform.PreviousHostPlayerId,
                transform.Changed,
                []);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or ArgumentException)
        {
            return new HostPreparation(false, HostPreparationOutcome.Failed, null, null, null, null, false, [exception.Message]);
        }
    }

    public async Task<ManagedSlotBaselineResult> CreateManagedSlotBaselineAsync(
        ManagedSlotReference slot,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slot.LogicalName) ||
            string.IsNullOrWhiteSpace(slot.CurrentDisplayName) ||
            string.IsNullOrWhiteSpace(slot.DesiredDisplayName))
        {
            return ManagedSlotBaselineFailed("La référence du slot permanent est incomplète.");
        }
        if (ProbeProcesses().Count > 0)
        {
            return ManagedSlotBaselineFailed("Fermez complètement The Planet Crafter avant de créer la baseline du slot permanent.");
        }

        var detection = await DetectInstallationAsync(cancellationToken);
        if (detection.WgsRoot is null) return ManagedSlotBaselineFailed("Stockage WGS introuvable.");

        string fullOutputRoot;
        string resolvedOutputRoot;
        string resolvedWgsRoot;
        try
        {
            fullOutputRoot = Path.GetFullPath(outputRoot);
            var finalPathResolver = _options.FinalPathResolver ?? FileSafety.ResolveDirectoryLinks;
            resolvedOutputRoot = finalPathResolver(fullOutputRoot);
            resolvedWgsRoot = finalPathResolver(detection.WgsRoot);
        }
        catch (Exception exception) when (IsPathValidationException(exception))
        {
            return ManagedSlotBaselineFailed("Résolution physique du chemin impossible : " + exception.Message);
        }
        if (FileSafety.IsSameOrDescendant(fullOutputRoot, detection.WgsRoot) ||
            FileSafety.IsSameOrDescendant(detection.WgsRoot, fullOutputRoot) ||
            FileSafety.IsSameOrDescendant(resolvedOutputRoot, resolvedWgsRoot) ||
            FileSafety.IsSameOrDescendant(resolvedWgsRoot, resolvedOutputRoot))
        {
            return ManagedSlotBaselineFailed("Le dossier de baseline doit être totalement séparé du stockage WGS.");
        }

        var before = await InspectLocalStorageAsync(cancellationToken);
        if (!before.Stable || before.GameRunning || before.Warnings.Count > 0)
        {
            return ManagedSlotBaselineFailed(
                "Le stockage WGS n'est pas stable, le jeu est encore ouvert ou l'inspection contient des éléments non interprétables : " +
                string.Join("; ", before.Warnings));
        }

        var matchingTargets = before.Worlds
            .Where(world => world.LogicalName.Equals(slot.LogicalName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingTargets.Length != 1)
        {
            return ManagedSlotBaselineFailed(
                matchingTargets.Length == 0
                    ? $"Le slot logique déclaré '{slot.LogicalName}' est absent."
                    : $"Le slot logique déclaré '{slot.LogicalName}' est ambigu.");
        }

        var target = matchingTargets[0];
        if (!target.DisplayName.Equals(slot.CurrentDisplayName, StringComparison.Ordinal))
        {
            return ManagedSlotBaselineFailed(
                $"Le nom affiché courant du slot '{slot.LogicalName}' ne correspond pas à la référence déclarée.");
        }
        var localPlayers = target.Players.Where(player => player.Id == 0).ToArray();
        if (localPlayers.Length != 1 || !localPlayers[0].IsHost || target.Players.Count(player => player.IsHost) != 1)
        {
            return ManagedSlotBaselineFailed(
                "Le slot permanent doit contenir un joueur local ID 0 qui soit l'unique hôte.");
        }

        Directory.CreateDirectory(fullOutputRoot);
        var snapshotId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
        var temporary = Path.Combine(fullOutputRoot, $".{snapshotId}.partial");
        var destination = Path.Combine(fullOutputRoot, snapshotId);
        try
        {
            Directory.CreateDirectory(temporary);
            foreach (var file in before.Files)
            {
                var source = ResolveContainedPath(detection.WgsRoot, file.RelativePath);
                var copied = ResolveContainedPath(Path.Combine(temporary, "wgs"), file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
                File.Copy(source, copied, overwrite: false);
                var copiedHash = await FileSafety.ComputeSha256Async(copied, cancellationToken);
                if (!copiedHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"Hash différent après copie : {file.RelativePath}");
                }
            }

            if (ProbeProcesses().Count > 0) throw new IOException("Le jeu a été lancé pendant la baseline du slot permanent.");
            var after = await InspectLocalStorageAsync(cancellationToken);
            if (!after.Stable || after.GameRunning || after.Warnings.Count > 0)
            {
                throw new IOException(
                    "La seconde observation de la baseline n'est pas sûre : jeu ouvert, stockage instable ou warnings WGS : " +
                    string.Join("; ", after.Warnings));
            }
            if (!Equivalent(before.Files, after.Files)) throw new IOException("Le stockage WGS a changé pendant la baseline du slot permanent.");

            var protectedWorlds = before.Worlds
                .Where(world => !world.LogicalName.Equals(target.LogicalName, StringComparison.OrdinalIgnoreCase))
                .Select(world => new ImportProtectedWorld(
                    world.LogicalName,
                    world.DisplayName,
                    world.WorldSeed,
                    GetWorldPayloadHash(before, world)))
                .OrderBy(world => world.LogicalName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var manifest = new ManagedSlotBaselineManifest(
                1,
                snapshotId,
                AdapterId,
                _options.PackageFamilyName,
                DateTimeOffset.UtcNow,
                new ManagedSlotBaselineTarget(
                    target.LogicalName,
                    target.DisplayName,
                    slot.DesiredDisplayName,
                    target.WorldSeed,
                    GetWorldPayloadHash(before, target)),
                protectedWorlds,
                before.Files);
            await File.WriteAllTextAsync(
                Path.Combine(temporary, "managed-slot-baseline.json"),
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken);
            var validation = await ValidateManagedSlotBaselineSourceAsync(temporary, cancellationToken);
            if (validation.Manifest is null)
            {
                throw new InvalidDataException(string.Join("; ", validation.Errors));
            }
            Directory.Move(temporary, destination);
            return new ManagedSlotBaselineResult(true, destination, manifest, []);
        }
        catch (Exception exception) when (exception is InvalidDataException || IsPathValidationException(exception))
        {
            string? cleanupError = null;
            if (Directory.Exists(temporary))
            {
                try
                {
                    Directory.Delete(temporary, recursive: true);
                }
                catch (Exception cleanupException)
                {
                    cleanupError = cleanupException.Message;
                }
            }
            return ManagedSlotBaselineFailed(
                cleanupError is null
                    ? exception.Message
                    : exception.Message + "; nettoyage de la baseline partielle impossible : " + cleanupError);
        }
    }

    public async Task<PortableImportResult> ReplaceManagedSlotAsync(
        PortableSaveArtifact artifact,
        string baselineDirectory,
        ManagedSlotReference slot,
        string expectedPlayerName,
        string preImportBackupOutputRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedPlayerName)) return ImportFailed("Le pseudo du joueur local attendu est obligatoire.");
        if (ProbeProcesses().Count > 0) return ImportFailed("Fermez complètement The Planet Crafter avant le remplacement du slot permanent.");

        var artifactValidation = await ValidateArtifactAsync(artifact, cancellationToken);
        if (!artifactValidation.IsValid) return ImportFailed(artifactValidation.Errors);
        var (artifactManifest, payload) = await ReadPortableArtifactAsync(artifact.Path, cancellationToken);
        if (!artifactManifest.DisplayName.Equals(slot.DesiredDisplayName, StringComparison.Ordinal))
        {
            return ImportFailed(
                $"Le nom affiché de l'artefact préparé doit être exactement '{slot.DesiredDisplayName}'.");
        }

        var hostGuard = PlanetCrafterWorldTransformer.PrepareForHost(
            payload,
            artifactManifest.Players,
            expectedPlayerName,
            artifactManifest.DisplayName);
        if (!hostGuard.Success)
        {
            var topologyPrefix = hostGuard.Outcome == HostPreparationOutcome.InvalidPlayerTopology
                ? "La topologie des joueurs de l'artefact est invalide."
                : "L'artefact n'est pas importable pour ce joueur.";
            return ImportFailed([topologyPrefix, .. hostGuard.Errors]);
        }
        if (hostGuard.Outcome != HostPreparationOutcome.AlreadyHost || hostGuard.Changed)
        {
            return ImportFailed(
                "L'artefact n'a pas été préparé pour ce joueur : le joueur attendu doit déjà être ID 0 et l'unique hôte.");
        }

        var baselineValidation = await ValidateManagedSlotBaselineSourceAsync(baselineDirectory, cancellationToken);
        if (baselineValidation.Manifest is null) return ImportFailed(baselineValidation.Errors);
        var baseline = baselineValidation.Manifest;
        var referenceErrors = VerifyManagedSlotReference(baseline, slot);
        if (referenceErrors.Count > 0) return ImportFailed(referenceErrors);

        var detection = await DetectInstallationAsync(cancellationToken);
        if (detection.WgsRoot is null) return ImportFailed("Stockage WGS courant introuvable.");
        try
        {
            if (PathsOverlap(baselineDirectory, detection.WgsRoot))
            {
                return ImportFailed("La baseline du slot permanent doit être séparée du stockage WGS courant.");
            }
            if (PathsOverlap(preImportBackupOutputRoot, detection.WgsRoot))
            {
                return ImportFailed("Le dossier de snapshot pré-import doit être séparé du stockage WGS courant.");
            }
        }
        catch (Exception exception) when (IsPathValidationException(exception))
        {
            return ImportFailed("Résolution physique du chemin impossible : " + exception.Message);
        }

        LocalStorageInspection current;
        try
        {
            current = await InspectLocalStorageAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            return ImportFailed("Le stockage WGS est verrouillé par un autre remplacement : " + exception.Message);
        }
        if (!current.Stable || current.GameRunning) return ImportFailed("Le stockage WGS courant n'est pas stable.");
        if (current.Warnings.Count > 0)
        {
            return ImportFailed(["L'inspection WGS contient des warnings de métadonnées ou de conteneur.", .. current.Warnings]);
        }
        var topologyErrors = VerifyManagedSlotWorldTopology(baseline, current);
        if (topologyErrors.Count > 0) return ImportFailed(topologyErrors);
        var protectedErrors = VerifyProtectedWorlds(baseline, current);
        if (protectedErrors.Count > 0) return ImportFailed(protectedErrors);

        var target = current.Worlds.Single(world => world.LogicalName.Equals(baseline.Target.LogicalName, StringComparison.OrdinalIgnoreCase));
        var currentHash = GetWorldPayloadHash(current, target);
        var importedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
        if (currentHash.Equals(importedHash, StringComparison.OrdinalIgnoreCase))
        {
            var idempotentErrors = VerifyImportedManagedTarget(baseline, artifactManifest, current, target, importedHash);
            return idempotentErrors.Count == 0
                ? new PortableImportResult(
                    true,
                    target.LogicalName,
                    target.DisplayName,
                    null,
                    baseline.Target.BeforePayloadSha256,
                    importedHash,
                    [])
                : new PortableImportResult(
                    false,
                    target.LogicalName,
                    target.DisplayName,
                    null,
                    baseline.Target.BeforePayloadSha256,
                    null,
                    idempotentErrors);
        }
        if (!currentHash.Equals(baseline.Target.BeforePayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            return ImportFailed("Le contenu du slot permanent ne correspond plus à la baseline déclarée.");
        }
        if (!target.DisplayName.Equals(baseline.Target.CurrentDisplayName, StringComparison.Ordinal))
        {
            return ImportFailed("Le nom affiché courant du slot permanent ne correspond plus à la baseline.");
        }

        var preImportSnapshot = await CreateSafetySnapshotAsync(
            preImportBackupOutputRoot,
            target.DisplayName,
            cancellationToken);
        if (!preImportSnapshot.Success || preImportSnapshot.SnapshotDirectory is null)
        {
            return ImportFailed(["Impossible de créer le snapshot automatique juste avant le remplacement.", .. preImportSnapshot.Errors]);
        }

        var targetBlob = ResolveContainedPath(detection.WgsRoot, target.BlobRelativePath);
        var previousHash = await FileSafety.ComputeSha256Async(targetBlob, cancellationToken);
        var temporary = Path.Combine(Path.GetDirectoryName(targetBlob)!, $".gsh-managed-import-{Guid.NewGuid():N}.tmp");
        var writePerformed = false;
        try
        {
            await WriteBytesWithFlushAsync(payload, temporary, cancellationToken);
            if (!importedHash.Equals(await FileSafety.ComputeSha256Async(temporary, cancellationToken), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le hash du fichier temporaire de remplacement est invalide.");
            }

            var justBeforeWrite = await InspectLocalStorageAsync(cancellationToken);
            if (justBeforeWrite.GameRunning || !justBeforeWrite.Stable)
            {
                throw new IOException("Le jeu a été lancé ou WGS a changé juste avant l'écriture du slot permanent.");
            }
            if (justBeforeWrite.Warnings.Count > 0)
            {
                throw new IOException(
                    "L'inspection WGS contient des warnings de métadonnées ou de conteneur juste avant l'écriture : " +
                    string.Join("; ", justBeforeWrite.Warnings));
            }
            var lastTopologyErrors = VerifyManagedSlotWorldTopology(baseline, justBeforeWrite);
            if (lastTopologyErrors.Count > 0)
            {
                throw new IOException("La topologie WGS a changé juste avant l'écriture : " + string.Join("; ", lastTopologyErrors));
            }
            var lastProtectedErrors = VerifyProtectedWorlds(baseline, justBeforeWrite);
            if (lastProtectedErrors.Count > 0)
            {
                throw new IOException("Une sauvegarde protégée a changé juste avant l'écriture : " + string.Join("; ", lastProtectedErrors));
            }
            var lastTarget = justBeforeWrite.Worlds.Single(world =>
                world.LogicalName.Equals(baseline.Target.LogicalName, StringComparison.OrdinalIgnoreCase));
            var lastTargetHash = GetWorldPayloadHash(justBeforeWrite, lastTarget);
            if (!lastTargetHash.Equals(baseline.Target.BeforePayloadSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le slot permanent a changé juste avant l'écriture.");
            }
            var currentTargetBlob = ResolveContainedPath(detection.WgsRoot, lastTarget.BlobRelativePath);
            if (!Path.GetFullPath(currentTargetBlob).Equals(Path.GetFullPath(targetBlob), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le blob physique du slot permanent a tourné juste avant l'écriture.");
            }

            // Écriture atomique : un crash entre ces deux lignes laisse le contenu précédent intact
            // (le déplacement n'a jamais eu lieu) ; TransferTransitionGate garantit qu'aucune autre
            // opération de slot ne s'exécute en parallèle sur cette même machine pendant ce temps.
            File.Move(temporary, targetBlob, overwrite: true);
            writePerformed = true;
            var finalHash = await FileSafety.ComputeSha256Async(targetBlob, cancellationToken);
            if (!finalHash.Equals(importedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le hash après remplacement ne correspond pas au payload préparé.");
            }

            var after = await InspectLocalStorageAsync(cancellationToken);
            if (!after.Stable || after.GameRunning) throw new IOException("WGS n'est pas stable après le remplacement.");
            if (after.Warnings.Count > 0)
            {
                throw new IOException(
                    "L'inspection WGS finale contient des warnings de métadonnées ou de conteneur : " +
                    string.Join("; ", after.Warnings));
            }
            var afterTopologyErrors = VerifyManagedSlotWorldTopology(baseline, after);
            if (afterTopologyErrors.Count > 0)
            {
                throw new IOException("La topologie WGS a changé après le remplacement : " + string.Join("; ", afterTopologyErrors));
            }
            var afterProtectedErrors = VerifyProtectedWorlds(baseline, after);
            if (afterProtectedErrors.Count > 0)
            {
                throw new IOException("Une sauvegarde protégée a changé après le remplacement : " + string.Join("; ", afterProtectedErrors));
            }
            var importedWorld = after.Worlds.Single(world =>
                world.LogicalName.Equals(baseline.Target.LogicalName, StringComparison.OrdinalIgnoreCase));
            var importedErrors = VerifyImportedManagedTarget(baseline, artifactManifest, after, importedWorld, importedHash);
            if (importedErrors.Count > 0)
            {
                throw new IOException("Le slot permanent est invalide après le remplacement : " + string.Join("; ", importedErrors));
            }

            return new PortableImportResult(
                true,
                importedWorld.LogicalName,
                importedWorld.DisplayName,
                preImportSnapshot.SnapshotDirectory,
                previousHash,
                importedHash,
                []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            if (!writePerformed && File.Exists(temporary)) File.Delete(temporary);
            var errors = new List<string> { exception.Message };
            if (writePerformed)
            {
                try
                {
                    await RollBackLogicalWorldFromSnapshotAsync(
                        preImportSnapshot.SnapshotDirectory,
                        target.LogicalName,
                        targetBlob,
                        cancellationToken);
                    errors.Add("Le contenu précédent du slot permanent a été restauré automatiquement après l'échec.");
                }
                catch (Exception rollbackException) when (rollbackException is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
                {
                    errors.Add("ÉCHEC DU ROLLBACK AUTOMATIQUE : " + rollbackException.Message);
                }
            }
            return new PortableImportResult(
                false,
                target.LogicalName,
                target.DisplayName,
                preImportSnapshot.SnapshotDirectory,
                previousHash,
                null,
                errors);
        }
    }

    public async Task<ManagedSlotReconciliationResult> ReconcileManagedSlotReplacementAsync(
        PortableSaveArtifact artifact,
        string baselineDirectory,
        ManagedSlotReference slot,
        string expectedPlayerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedPlayerName))
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.InvalidArtifact,
                slot.LogicalName,
                null,
                null,
                ["Le pseudo du joueur local attendu est obligatoire."]);
        }
        if (ProbeProcesses().Count > 0)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.UnexpectedTargetContent,
                slot.LogicalName,
                null,
                null,
                ["Fermez complètement The Planet Crafter avant la réconciliation du slot permanent."]);
        }

        var artifactValidation = await ValidateArtifactAsync(artifact, cancellationToken);
        if (!artifactValidation.IsValid)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.InvalidArtifact,
                slot.LogicalName,
                null,
                null,
                artifactValidation.Errors);
        }

        PortableArtifactManifest artifactManifest;
        byte[] payload;
        try
        {
            (artifactManifest, payload) = await ReadPortableArtifactAsync(artifact.Path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.InvalidArtifact,
                slot.LogicalName,
                null,
                null,
                [exception.Message]);
        }
        var importedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
        if (!artifactManifest.DisplayName.Equals(slot.DesiredDisplayName, StringComparison.Ordinal))
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.InvalidArtifact,
                slot.LogicalName,
                null,
                importedHash,
                ["Le nom affiché de l'artefact ne correspond pas au nom permanent désiré."]);
        }
        var hostGuard = PlanetCrafterWorldTransformer.PrepareForHost(
            payload,
            artifactManifest.Players,
            expectedPlayerName,
            artifactManifest.DisplayName);
        if (!hostGuard.Success || hostGuard.Outcome != HostPreparationOutcome.AlreadyHost || hostGuard.Changed)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.InvalidArtifact,
                slot.LogicalName,
                null,
                importedHash,
                ["L'artefact n'est pas préparé pour le joueur attendu.", .. hostGuard.Errors]);
        }

        var baselineValidation = await ValidateManagedSlotBaselineSourceAsync(baselineDirectory, cancellationToken);
        if (baselineValidation.Manifest is null)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.InvalidBaseline,
                slot.LogicalName,
                null,
                importedHash,
                baselineValidation.Errors);
        }
        var baseline = baselineValidation.Manifest;
        var referenceErrors = VerifyManagedSlotReference(baseline, slot);
        if (referenceErrors.Count > 0)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.InvalidBaseline,
                slot.LogicalName,
                null,
                importedHash,
                referenceErrors);
        }

        var detection = await DetectInstallationAsync(cancellationToken);
        if (detection.WgsRoot is null)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.TargetMissing,
                baseline.Target.LogicalName,
                null,
                importedHash,
                ["Stockage WGS courant introuvable."]);
        }
        try
        {
            if (PathsOverlap(baselineDirectory, detection.WgsRoot))
            {
                return new ManagedSlotReconciliationResult(
                    ManagedSlotReconciliationState.InvalidBaseline,
                    baseline.Target.LogicalName,
                    null,
                    importedHash,
                    ["La baseline du slot permanent doit être séparée du stockage WGS courant."]);
            }
        }
        catch (Exception exception) when (IsPathValidationException(exception))
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.InvalidBaseline,
                baseline.Target.LogicalName,
                null,
                importedHash,
                ["Résolution physique de la baseline impossible : " + exception.Message]);
        }

        LocalStorageInspection current;
        try
        {
            current = await InspectLocalStorageAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.TargetMissing,
                baseline.Target.LogicalName,
                null,
                importedHash,
                [exception.Message]);
        }
        if (!current.Stable || current.GameRunning)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.UnexpectedTargetContent,
                baseline.Target.LogicalName,
                null,
                importedHash,
                ["Le stockage WGS courant n'est pas stable."]);
        }
        if (current.Warnings.Count > 0)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.UnexpectedTargetContent,
                baseline.Target.LogicalName,
                null,
                importedHash,
                ["L'inspection WGS contient des warnings de métadonnées ou de conteneur.", .. current.Warnings]);
        }

        var protectedErrors = VerifyProtectedWorlds(baseline, current);
        if (protectedErrors.Count > 0)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.ProtectedWorldChanged,
                baseline.Target.LogicalName,
                null,
                importedHash,
                protectedErrors);
        }

        var targets = current.Worlds
            .Where(world => world.LogicalName.Equals(baseline.Target.LogicalName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (targets.Length == 0)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.TargetMissing,
                baseline.Target.LogicalName,
                null,
                importedHash,
                ["Le slot logique déclaré n'existe plus."]);
        }
        if (targets.Length != 1)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.UnexpectedTargetContent,
                baseline.Target.LogicalName,
                null,
                importedHash,
                ["Le slot logique déclaré est ambigu."]);
        }

        var topologyErrors = VerifyManagedSlotWorldTopology(baseline, current);
        var target = targets[0];
        var currentHash = GetWorldPayloadHash(current, target);
        if (topologyErrors.Count > 0)
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.UnexpectedTargetContent,
                baseline.Target.LogicalName,
                currentHash,
                importedHash,
                topologyErrors);
        }
        if (currentHash.Equals(baseline.Target.BeforePayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ManagedSlotReconciliationResult(
                ManagedSlotReconciliationState.PreviousPayloadPresent,
                baseline.Target.LogicalName,
                currentHash,
                importedHash,
                []);
        }
        if (currentHash.Equals(importedHash, StringComparison.OrdinalIgnoreCase))
        {
            var importedErrors = VerifyImportedManagedTarget(baseline, artifactManifest, current, target, importedHash);
            return importedErrors.Count == 0
                ? new ManagedSlotReconciliationResult(
                    ManagedSlotReconciliationState.ImportedPayloadPresent,
                    baseline.Target.LogicalName,
                    currentHash,
                    importedHash,
                    [])
                : new ManagedSlotReconciliationResult(
                    ManagedSlotReconciliationState.UnexpectedTargetContent,
                    baseline.Target.LogicalName,
                    currentHash,
                    importedHash,
                    importedErrors);
        }

        return new ManagedSlotReconciliationResult(
            ManagedSlotReconciliationState.UnexpectedTargetContent,
            baseline.Target.LogicalName,
            currentHash,
            importedHash,
            ["Le slot cible ne contient ni le payload précédent de la baseline ni l'artefact préparé."]);
    }

    public async Task<ImportBaselineResult> CreateImportBaselineAsync(
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        if (ProbeProcesses().Count > 0) return ImportBaselineFailed("Fermez complètement The Planet Crafter avant de créer la baseline d'import.");
        var detection = await DetectInstallationAsync(cancellationToken);
        if (detection.WgsRoot is null) return ImportBaselineFailed("Stockage WGS introuvable.");

        var fullOutputRoot = Path.GetFullPath(outputRoot);
        if (FileSafety.IsSameOrDescendant(fullOutputRoot, detection.WgsRoot) || FileSafety.IsSameOrDescendant(detection.WgsRoot, fullOutputRoot))
        {
            return ImportBaselineFailed("Le dossier de baseline doit être totalement séparé du stockage WGS.");
        }

        var before = await InspectLocalStorageAsync(cancellationToken);
        if (!before.Stable || before.GameRunning) return ImportBaselineFailed("Le stockage WGS n'est pas stable ou le jeu est encore ouvert.");

        Directory.CreateDirectory(fullOutputRoot);
        var snapshotId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
        var temporary = Path.Combine(fullOutputRoot, $".{snapshotId}.partial");
        var destination = Path.Combine(fullOutputRoot, snapshotId);
        try
        {
            Directory.CreateDirectory(temporary);
            foreach (var file in before.Files)
            {
                var source = ResolveContainedPath(detection.WgsRoot, file.RelativePath);
                var target = ResolveContainedPath(Path.Combine(temporary, "wgs"), file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: false);
                var copiedHash = await FileSafety.ComputeSha256Async(target, cancellationToken);
                if (!copiedHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"Hash différent après copie : {file.RelativePath}");
                }
            }

            if (ProbeProcesses().Count > 0) throw new IOException("Le jeu a été lancé pendant la baseline.");
            var after = await InspectLocalStorageAsync(cancellationToken);
            if (!Equivalent(before.Files, after.Files)) throw new IOException("Le stockage WGS a changé pendant la baseline.");

            var protectedWorlds = before.Worlds
                .Select(world => new ImportProtectedWorld(world.LogicalName, world.DisplayName, world.WorldSeed, GetWorldPayloadHash(before, world)))
                .OrderBy(world => world.LogicalName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var manifest = new ImportBaselineManifest(
                1,
                snapshotId,
                AdapterId,
                _options.PackageFamilyName,
                DateTimeOffset.UtcNow,
                before.Worlds.Select(world => ParseStandardIndex(world.LogicalName) ?? 0).DefaultIfEmpty(0).Max(),
                protectedWorlds,
                before.Files);
            await File.WriteAllTextAsync(
                Path.Combine(temporary, "import-baseline.json"),
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken);
            Directory.Move(temporary, destination);
            return new ImportBaselineResult(true, destination, manifest, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            return ImportBaselineFailed(exception.Message);
        }
    }

    public async Task<ImportTargetProbeResult> ProbeImportTargetAsync(
        string baselineDirectory,
        string expectedPlaceholderName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedPlaceholderName))
        {
            return new ImportTargetProbeResult(false, null, null, null, ["Le nom du placeholder attendu est obligatoire."]);
        }
        if (ProbeProcesses().Count > 0)
        {
            return new ImportTargetProbeResult(false, null, null, null, ["Fermez complètement The Planet Crafter avant de valider le placeholder."]);
        }

        var baselineValidation = await ValidateImportBaselineSourceAsync(baselineDirectory, cancellationToken);
        if (baselineValidation.Manifest is null)
        {
            return new ImportTargetProbeResult(false, null, null, null, baselineValidation.Errors);
        }
        var baseline = baselineValidation.Manifest;
        var current = await InspectLocalStorageAsync(cancellationToken);
        if (!current.Stable || current.GameRunning)
        {
            return new ImportTargetProbeResult(false, null, null, null, ["Le stockage WGS courant n'est pas stable."]);
        }
        var protectedCheck = VerifyProtectedWorlds(baseline, current);
        if (protectedCheck.Count > 0)
        {
            return new ImportTargetProbeResult(false, null, null, null, protectedCheck);
        }

        var baselineNames = baseline.ProtectedWorlds.Select(world => world.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedWorlds = current.Worlds.Where(world => !baselineNames.Contains(world.LogicalName)).ToArray();
        if (addedWorlds.Length != 1)
        {
            return new ImportTargetProbeResult(false, null, null, null,
                [$"Un seul nouveau Standard-X est attendu après la baseline; détectés : {addedWorlds.Length}."]);
        }

        var target = addedWorlds[0];
        if (!DisplayNamesEquivalent(target.DisplayName, expectedPlaceholderName))
        {
            return new ImportTargetProbeResult(false, target.LogicalName, target.DisplayName, null,
                [$"Le nouveau monde ne correspond pas au placeholder attendu '{expectedPlaceholderName}'."]);
        }
        var targetIndex = ParseStandardIndex(target.LogicalName);
        if (targetIndex is null || targetIndex <= baseline.MaximumStandardIndex)
        {
            return new ImportTargetProbeResult(false, target.LogicalName, target.DisplayName, null,
                ["Le slot cible n'est pas un nouveau Standard-X d'index supérieur à la baseline."]);
        }
        if (target.Players.Count != 1 || target.Players[0].Id != 0 || !target.Players[0].IsHost)
        {
            return new ImportTargetProbeResult(false, target.LogicalName, target.DisplayName, null,
                ["Le nouveau monde cible n'a pas la topologie attendue d'un placeholder local neuf (un joueur ID 0 hôte)."]);
        }

        var placeholderHash = GetWorldPayloadHash(current, target);
        return new ImportTargetProbeResult(true, target.LogicalName, target.DisplayName, placeholderHash, []);
    }

    public async Task<ImportReconciliationResult> ReconcilePortableImportAsync(
        PortableSaveArtifact artifact,
        string baselineDirectory,
        string expectedPlayerName,
        string targetLogicalName,
        string placeholderPayloadSha256,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedPlayerName) || string.IsNullOrWhiteSpace(targetLogicalName) || string.IsNullOrWhiteSpace(placeholderPayloadSha256))
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.InvalidArtifact,
                targetLogicalName,
                null,
                null,
                ["Les paramètres de réconciliation sont incomplets."]);
        }
        if (ProbeProcesses().Count > 0)
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.UnexpectedTargetContent,
                targetLogicalName,
                null,
                null,
                ["Fermez complètement The Planet Crafter avant la réconciliation d'import."]);
        }

        var artifactValidation = await ValidateArtifactAsync(artifact, cancellationToken);
        if (!artifactValidation.IsValid)
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.InvalidArtifact,
                targetLogicalName,
                null,
                null,
                artifactValidation.Errors);
        }
        var (artifactManifest, payload) = await ReadPortableArtifactAsync(artifact.Path, cancellationToken);
        var hostGuard = PlanetCrafterWorldTransformer.PrepareForHost(payload, artifactManifest.Players, expectedPlayerName, artifactManifest.DisplayName);
        if (!hostGuard.Success || hostGuard.Outcome != HostPreparationOutcome.AlreadyHost || hostGuard.Changed)
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.InvalidArtifact,
                targetLogicalName,
                null,
                null,
                ["L'artefact n'est pas préparé pour le joueur attendu.", .. hostGuard.Errors]);
        }

        var baselineValidation = await ValidateImportBaselineSourceAsync(baselineDirectory, cancellationToken);
        if (baselineValidation.Manifest is null)
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.InvalidBaseline,
                targetLogicalName,
                null,
                null,
                baselineValidation.Errors);
        }
        var baseline = baselineValidation.Manifest;
        var current = await InspectLocalStorageAsync(cancellationToken);
        if (!current.Stable || current.GameRunning)
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.UnexpectedTargetContent,
                targetLogicalName,
                null,
                null,
                ["Le stockage WGS courant n'est pas stable."]);
        }
        var protectedCheck = VerifyProtectedWorlds(baseline, current);
        if (protectedCheck.Count > 0)
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.ProtectedWorldChanged,
                targetLogicalName,
                null,
                null,
                protectedCheck);
        }

        var target = current.Worlds.SingleOrDefault(world => world.LogicalName.Equals(targetLogicalName, StringComparison.OrdinalIgnoreCase));
        var importedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
        if (target is null)
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.TargetMissing,
                targetLogicalName,
                null,
                importedHash,
                ["Le slot cible n'existe plus."]);
        }
        if (baseline.ProtectedWorlds.Any(world => world.LogicalName.Equals(target.LogicalName, StringComparison.OrdinalIgnoreCase)))
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.UnexpectedTargetContent,
                targetLogicalName,
                null,
                importedHash,
                ["Le slot cible appartient à la baseline protégée."]);
        }
        var targetIndex = ParseStandardIndex(target.LogicalName);
        if (targetIndex is null || targetIndex <= baseline.MaximumStandardIndex)
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.UnexpectedTargetContent,
                targetLogicalName,
                null,
                importedHash,
                ["Le slot cible n'est pas un nouveau Standard-X valide."]);
        }

        var currentHash = GetWorldPayloadHash(current, target);
        if (currentHash.Equals(importedHash, StringComparison.OrdinalIgnoreCase))
        {
            if (!target.DisplayName.Equals(artifactManifest.DisplayName, StringComparison.Ordinal) ||
                target.WorldSeed != artifactManifest.WorldSeed ||
                !PlayersEquivalent(target.Players, artifactManifest.Players))
            {
                return new ImportReconciliationResult(
                    ImportReconciliationState.UnexpectedTargetContent,
                    targetLogicalName,
                    currentHash,
                    importedHash,
                    ["Le hash importé est présent mais la structure sémantique du monde ne correspond pas à l'artefact préparé."]);
            }
            return new ImportReconciliationResult(
                ImportReconciliationState.ImportedArtifactPresent,
                targetLogicalName,
                currentHash,
                importedHash,
                []);
        }
        if (currentHash.Equals(placeholderPayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ImportReconciliationResult(
                ImportReconciliationState.PlaceholderIntact,
                targetLogicalName,
                currentHash,
                importedHash,
                []);
        }

        return new ImportReconciliationResult(
            ImportReconciliationState.UnexpectedTargetContent,
            targetLogicalName,
            currentHash,
            importedHash,
            ["Le slot cible ne contient ni le placeholder connu ni l'artefact préparé."]);
    }

    public async Task<PortableImportResult> ImportPortableArtifactAsync(
        PortableSaveArtifact artifact,
        string baselineDirectory,
        string expectedPlayerName,
        string expectedPlaceholderName,
        string preImportBackupOutputRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedPlayerName)) return ImportFailed("Le pseudo du joueur local attendu est obligatoire.");
        if (string.IsNullOrWhiteSpace(expectedPlaceholderName)) return ImportFailed("Le nom du nouveau monde placeholder est obligatoire.");
        if (ProbeProcesses().Count > 0) return ImportFailed("Fermez complètement The Planet Crafter avant l'import.");

        var artifactValidation = await ValidateArtifactAsync(artifact, cancellationToken);
        if (!artifactValidation.IsValid) return ImportFailed(artifactValidation.Errors);
        var (artifactManifest, payload) = await ReadPortableArtifactAsync(artifact.Path, cancellationToken);
        var hostGuard = PlanetCrafterWorldTransformer.PrepareForHost(payload, artifactManifest.Players, expectedPlayerName, artifactManifest.DisplayName);
        if (!hostGuard.Success)
        {
            return ImportFailed(["L'artefact n'est pas importable pour ce joueur.", .. hostGuard.Errors]);
        }
        if (hostGuard.Outcome != HostPreparationOutcome.AlreadyHost || hostGuard.Changed)
        {
            return ImportFailed("L'artefact n'a pas été préparé pour ce joueur : le joueur attendu doit déjà être ID 0 et unique hôte avant l'import.");
        }

        var baselineValidation = await ValidateImportBaselineSourceAsync(baselineDirectory, cancellationToken);
        if (baselineValidation.Manifest is null) return ImportFailed(baselineValidation.Errors);
        var baseline = baselineValidation.Manifest;

        var detection = await DetectInstallationAsync(cancellationToken);
        if (detection.WgsRoot is null) return ImportFailed("Stockage WGS courant introuvable.");
        var current = await InspectLocalStorageAsync(cancellationToken);
        if (!current.Stable || current.GameRunning) return ImportFailed("Le stockage WGS courant n'est pas stable.");

        var protectedCheck = VerifyProtectedWorlds(baseline, current);
        if (protectedCheck.Count > 0) return ImportFailed(protectedCheck);

        var baselineNames = baseline.ProtectedWorlds.Select(world => world.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedWorlds = current.Worlds.Where(world => !baselineNames.Contains(world.LogicalName)).ToArray();
        if (addedWorlds.Length != 1) return ImportFailed($"Un seul nouveau Standard-X est attendu après la baseline; détectés : {addedWorlds.Length}.");

        var target = addedWorlds[0];
        if (!DisplayNamesEquivalent(target.DisplayName, expectedPlaceholderName))
        {
            return ImportFailed($"Le nouveau monde ne correspond pas au placeholder attendu '{expectedPlaceholderName}'.");
        }
        var targetIndex = ParseStandardIndex(target.LogicalName);
        if (targetIndex is null || targetIndex <= baseline.MaximumStandardIndex)
        {
            return ImportFailed("Le slot cible n'est pas un nouveau Standard-X d'index supérieur à la baseline.");
        }
        if (target.Players.Count != 1 || target.Players[0].Id != 0 || !target.Players[0].IsHost)
        {
            return ImportFailed("Le nouveau monde cible n'a pas la topologie attendue d'un placeholder local neuf (un joueur ID 0 hôte).");
        }

        var preImportSnapshot = await CreateSafetySnapshotAsync(preImportBackupOutputRoot, target.DisplayName, cancellationToken);
        if (!preImportSnapshot.Success || preImportSnapshot.SnapshotDirectory is null)
        {
            return ImportFailed(["Impossible de créer le snapshot automatique juste avant l'import.", .. preImportSnapshot.Errors]);
        }

        var targetBlob = ResolveContainedPath(detection.WgsRoot, target.BlobRelativePath);
        var previousHash = await FileSafety.ComputeSha256Async(targetBlob, cancellationToken);
        var importedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
        var temporary = Path.Combine(Path.GetDirectoryName(targetBlob)!, $".gsh-import-{Guid.NewGuid():N}.tmp");
        var writePerformed = false;
        try
        {
            await WriteBytesWithFlushAsync(payload, temporary, cancellationToken);
            if (!importedHash.Equals(await FileSafety.ComputeSha256Async(temporary, cancellationToken), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le hash du fichier temporaire d'import est invalide.");
            }

            var justBeforeWrite = await InspectLocalStorageAsync(cancellationToken);
            if (justBeforeWrite.GameRunning || !justBeforeWrite.Stable) throw new IOException("Le jeu a été lancé ou WGS a changé juste avant l'écriture.");
            var lastProtectedCheck = VerifyProtectedWorlds(baseline, justBeforeWrite);
            if (lastProtectedCheck.Count > 0) throw new IOException("Une sauvegarde protégée a changé juste avant l'import : " + string.Join("; ", lastProtectedCheck));
            var lastTarget = justBeforeWrite.Worlds.SingleOrDefault(world => world.LogicalName.Equals(target.LogicalName, StringComparison.OrdinalIgnoreCase));
            if (lastTarget is null || !DisplayNamesEquivalent(lastTarget.DisplayName, expectedPlaceholderName))
            {
                throw new IOException("Le slot cible a changé juste avant l'import.");
            }
            var lastTargetHash = GetWorldPayloadHash(justBeforeWrite, lastTarget);
            if (!lastTargetHash.Equals(previousHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le contenu du placeholder a changé juste avant l'import.");
            }

            var currentTargetBlob = ResolveContainedPath(detection.WgsRoot, lastTarget.BlobRelativePath);
            if (!Path.GetFullPath(currentTargetBlob).Equals(Path.GetFullPath(targetBlob), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le blob physique du placeholder a tourné juste avant l'import; recommencez depuis une nouvelle baseline.");
            }

            File.Move(temporary, targetBlob, overwrite: true);
            writePerformed = true;
            var finalHash = await FileSafety.ComputeSha256Async(targetBlob, cancellationToken);
            if (!finalHash.Equals(importedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Le hash après import ne correspond pas au payload attendu.");
            }

            var after = await InspectLocalStorageAsync(cancellationToken);
            if (!after.Stable || after.GameRunning) throw new IOException("WGS n'est pas stable après l'import.");
            var afterProtectedCheck = VerifyProtectedWorlds(baseline, after);
            if (afterProtectedCheck.Count > 0) throw new IOException("Une sauvegarde protégée a changé pendant l'import : " + string.Join("; ", afterProtectedCheck));
            var importedWorld = after.Worlds.SingleOrDefault(world => world.LogicalName.Equals(target.LogicalName, StringComparison.OrdinalIgnoreCase));
            if (importedWorld is null ||
                !importedWorld.DisplayName.Equals(artifactManifest.DisplayName, StringComparison.Ordinal) ||
                importedWorld.WorldSeed != artifactManifest.WorldSeed ||
                !PlayersEquivalent(importedWorld.Players, artifactManifest.Players))
            {
                throw new IOException("Le monde relu après import ne correspond pas à l'artefact préparé.");
            }

            return new PortableImportResult(true, target.LogicalName, importedWorld.DisplayName, preImportSnapshot.SnapshotDirectory, previousHash, importedHash, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            var errors = new List<string> { exception.Message };
            if (writePerformed)
            {
                try
                {
                    await RollBackLogicalWorldFromSnapshotAsync(preImportSnapshot.SnapshotDirectory, target.LogicalName, targetBlob, cancellationToken);
                    errors.Add("Le placeholder d'origine a été restauré automatiquement après l'échec.");
                }
                catch (Exception rollbackException) when (rollbackException is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    errors.Add("ÉCHEC DU ROLLBACK AUTOMATIQUE : " + rollbackException.Message);
                }
            }
            return new PortableImportResult(false, target.LogicalName, target.DisplayName, preImportSnapshot.SnapshotDirectory, previousHash, null, errors);
        }
    }

    public async Task<GameLaunch> LaunchGameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packageFamily = _options.InstalledPackageFamilyProbe?.Invoke();
        var applicationId = _options.InstalledApplicationIdProbe?.Invoke();
        if (_options.InstalledPackageFamilyProbe is null)
        {
            var registration = FindAppxRegistration();
            var registered = registration.PackageFullName is not null;
            var packageRootExists = Directory.Exists(GetPackageRoot());
            packageFamily = registered || packageRootExists ? _options.PackageFamilyName : null;
            applicationId ??= FindInstalledApplicationId(registration.InstallLocation);
        }

        if (string.IsNullOrWhiteSpace(packageFamily))
        {
            return new GameLaunch(
                false,
                null,
                ["The Planet Crafter n'est pas installé depuis l'application Xbox pour ce compte Windows."]);
        }

        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return new GameLaunch(
                false,
                null,
                ["L'application Xbox de The Planet Crafter n'a pas pu être identifiée dans son manifeste installé."]);
        }

        var aumid = $"{packageFamily.Trim()}!{applicationId.Trim()}";
        try
        {
            (_options.AppActivator ?? ActivateXboxApplication)(aumid);
            var attempts = Math.Max(1, _options.LaunchVerificationAttempts);
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var processes = ProbeProcesses();
                if (processes.Count > 0)
                    return new GameLaunch(true, processes[0].Id, []);
                if (attempt + 1 < attempts && _options.LaunchVerificationInterval > TimeSpan.Zero)
                    await Task.Delay(_options.LaunchVerificationInterval, cancellationToken);
            }
            return new GameLaunch(
                false,
                null,
                ["Xbox a accepté la demande, mais The Planet Crafter n'a pas démarré. Ouvrez l'application Xbox pour réessayer."]);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new GameLaunch(
                false,
                null,
                [$"Impossible de lancer le jeu depuis Xbox : {exception.Message}"]);
        }
    }

    private static int? ActivateXboxApplication(string aumid)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Le lancement Xbox nécessite Windows.");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = $"shell:AppsFolder\\{aumid}",
            UseShellExecute = true
        });
        return process?.Id;
    }

    public async Task<SaveStability> WaitForSaveStabilityAsync(
        TimeSpan observationWindow,
        CancellationToken cancellationToken = default)
    {
        if (observationWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(observationWindow), "La fenêtre d'observation doit être positive.");
        }
        if (ProbeProcesses().Count > 0)
        {
            return new SaveStability(false, ["game-running"]);
        }

        var before = await InspectLocalStorageAsync(cancellationToken);
        await Task.Delay(observationWindow, cancellationToken);
        if (ProbeProcesses().Count > 0)
        {
            return new SaveStability(false, ["game-started-during-observation"]);
        }
        var after = await InspectLocalStorageAsync(cancellationToken);

        var beforeFiles = before.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var afterFiles = after.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var paths = beforeFiles.Keys.Concat(afterFiles.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var changed = new List<string>();
        foreach (var path in paths)
        {
            if (!beforeFiles.TryGetValue(path, out var left) ||
                !afterFiles.TryGetValue(path, out var right) ||
                left.Length != right.Length ||
                !left.Sha256.Equals(right.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                changed.Add(path);
            }
        }
        return new SaveStability(before.Stable && after.Stable && changed.Count == 0, changed);
    }

    private string GetPackageRoot()
    {
        var localAppData = _options.LocalApplicationDataOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Packages", _options.PackageFamilyName);
    }

    private (string? PackageFullName, string? Version, string? InstallLocation) FindAppxRegistration()
    {
        if (!OperatingSystem.IsWindows() || _options.LocalApplicationDataOverride is not null)
        {
            return (null, null, null);
        }

        const string repositoryPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        using var repository = Registry.CurrentUser.OpenSubKey(repositoryPath, writable: false);
        var publisherId = _options.PackageFamilyName.Split('_').Last();
        var prefix = _options.PackageFamilyName.Split('_').First() + "_";
        var packageName = repository?.GetSubKeyNames()
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith($"__{publisherId}", StringComparison.OrdinalIgnoreCase))
            .OrderDescending(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (packageName is null) return (null, null, null);

        using var package = repository!.OpenSubKey(packageName, writable: false);
        var packageId = package?.GetValue("PackageID") as string ?? packageName;
        var installLocation = package?.GetValue("PackageRootFolder") as string;
        var segments = packageId.Split('_');
        var version = segments.Length > 1 ? segments[1] : null;
        return (packageId, version, installLocation);
    }

    private static string? FindInstalledApplicationId(string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation)) return null;
        var manifestPath = Path.Combine(installLocation, "AppxManifest.xml");
        if (!File.Exists(manifestPath)) return null;

        try
        {
            return XDocument.Load(manifestPath)
                .Descendants()
                .Where(element => element.Name.LocalName.Equals("Application", StringComparison.Ordinal))
                .Select(element => element.Attribute("Id")?.Value)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private IReadOnlyList<(int Id, string Name)> ProbeProcesses()
    {
        if (_options.ProcessProbe is not null)
        {
            return _options.ProcessProbe();
        }

        return Process.GetProcesses()
            .Where(process => process.ProcessName.Equals("Planet Crafter", StringComparison.OrdinalIgnoreCase))
            .Select(process => (process.Id, process.ProcessName))
            .ToArray();
    }

    private bool HasActiveNetworkRoute()
    {
        if (_options.ActiveNetworkRouteProbe is not null) return _options.ActiveNetworkRouteProbe();
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.NetworkInterfaceType != NetworkInterfaceType.Loopback && network.OperationalStatus == OperationalStatus.Up)
            .Any(network => network.GetIPProperties().GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
                !gateway.Address.Equals(System.Net.IPAddress.Any) &&
                !gateway.Address.Equals(System.Net.IPAddress.IPv6Any)));
    }

    private static DiagnosticFileRole Classify(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("containers.index", StringComparison.OrdinalIgnoreCase)) return DiagnosticFileRole.ContainerIndex;
        if (name.StartsWith("container.", StringComparison.OrdinalIgnoreCase)) return DiagnosticFileRole.ContainerMetadata;
        if (name.Length == 32 && name.All(Uri.IsHexDigit)) return DiagnosticFileRole.OpaqueBlob;
        return DiagnosticFileRole.Unknown;
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            FileSafety.RejectReparsePoint(directory);
            foreach (var file in directory.EnumerateFiles())
            {
                FileSafety.RejectReparsePoint(file);
                yield return file.FullName;
            }
            foreach (var child in directory.EnumerateDirectories())
            {
                FileSafety.RejectReparsePoint(child);
                pending.Push(child);
            }
        }
    }

    private static async Task<(IReadOnlyList<DiscoveredWorld> Worlds, IReadOnlyList<string> Warnings)> DiscoverWorldsAsync(
        string wgsRoot,
        CancellationToken cancellationToken)
    {
        var worlds = new List<DiscoveredWorld>();
        var warnings = new List<string>();
        foreach (var metadataPath in EnumerateSafeFiles(wgsRoot).Where(path => Path.GetFileName(path).StartsWith("container.", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] metadata;
            try
            {
                metadata = await File.ReadAllBytesAsync(metadataPath, cancellationToken);
            }
            catch (IOException exception)
            {
                warnings.Add($"Métadonnées illisibles : {Path.GetFileName(metadataPath)} ({exception.Message})");
                continue;
            }

            if (metadata.Length < 8) continue;
            var entryCount = BitConverter.ToInt32(metadata, 4);
            if (entryCount is < 0 or > 1024 || metadata.Length < 8 + entryCount * 160)
            {
                warnings.Add($"Format de conteneur inattendu : {Path.GetFileName(metadataPath)}");
                continue;
            }

            for (var index = 0; index < entryCount; index++)
            {
                var offset = 8 + index * 160;
                var logicalName = ReadNullTerminatedUnicode(metadata.AsSpan(offset, 128));
                if (!logicalName.StartsWith("Standard-", StringComparison.OrdinalIgnoreCase) || !logicalName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                var blobPath = ResolveCurrentBlobPath(metadataPath, metadata, offset);
                if (!File.Exists(blobPath))
                {
                    warnings.Add($"Blob absent pour {logicalName}.");
                    continue;
                }

                var discovered = await ReadWorldMetadataAsync(wgsRoot, logicalName, blobPath, cancellationToken);
                if (discovered is null)
                {
                    warnings.Add($"Métadonnées de monde non reconnues : {logicalName}.");
                    continue;
                }
                worlds.Add(discovered);
            }
        }

        return (worlds.OrderBy(world => world.LogicalName, StringComparer.OrdinalIgnoreCase).ToArray(), warnings);
    }

    private static async Task<IReadOnlyDictionary<string, LogicalFileState>> DiscoverLogicalFilesAsync(
        string wgsRoot,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, LogicalFileState>(StringComparer.OrdinalIgnoreCase);
        foreach (var metadataPath in EnumerateSafeFiles(wgsRoot).Where(path => Path.GetFileName(path).StartsWith("container.", StringComparison.OrdinalIgnoreCase)))
        {
            var metadata = await File.ReadAllBytesAsync(metadataPath, cancellationToken);
            if (metadata.Length < 8) continue;
            var entryCount = BitConverter.ToInt32(metadata, 4);
            if (entryCount is < 0 or > 1024 || metadata.Length < 8 + entryCount * 160) throw new InvalidDataException($"Format de conteneur inattendu : {Path.GetFileName(metadataPath)}");
            for (var index = 0; index < entryCount; index++)
            {
                var offset = 8 + index * 160;
                var logicalName = ReadNullTerminatedUnicode(metadata.AsSpan(offset, 128));
                var blobPath = ResolveCurrentBlobPath(metadataPath, metadata, offset);
                if (!File.Exists(blobPath)) continue;
                if (result.ContainsKey(logicalName)) throw new InvalidDataException($"Nom logique WGS dupliqué : {logicalName}");
                result.Add(logicalName, new LogicalFileState(
                    await FileSafety.ComputeSha256Async(blobPath, cancellationToken),
                    new FileInfo(blobPath).Length));
            }
        }
        return result;
    }

    private static async Task<DiscoveredWorld?> ReadWorldMetadataAsync(
        string wgsRoot,
        string logicalName,
        string blobPath,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(blobPath);
        if (info.Length > 256L * 1024 * 1024) return null;
        var text = await File.ReadAllTextAsync(blobPath, cancellationToken);
        return PlanetCrafterWorldPayloadReader.Parse(
            logicalName,
            text,
            FileSafety.GetSafeRelativePath(wgsRoot, blobPath));
    }

    private static string ReadNullTerminatedUnicode(ReadOnlySpan<byte> bytes)
    {
        var length = 0;
        while (length + 1 < bytes.Length && (bytes[length] != 0 || bytes[length + 1] != 0)) length += 2;
        return System.Text.Encoding.Unicode.GetString(bytes[..length]);
    }

    private static string ResolveCurrentBlobPath(string metadataPath, byte[] metadata, int entryOffset)
    {
        var directory = Path.GetDirectoryName(metadataPath)!;
        var currentName = new Guid(metadata.AsSpan(entryOffset + 144, 16)).ToString("N").ToUpperInvariant();
        var currentPath = Path.Combine(directory, currentName);
        if (File.Exists(currentPath)) return currentPath;
        var stableName = new Guid(metadata.AsSpan(entryOffset + 128, 16)).ToString("N").ToUpperInvariant();
        return Path.Combine(directory, stableName);
    }

    private static bool PlayersEquivalent(IReadOnlyList<DiscoveredPlayer> left, IReadOnlyList<DiscoveredPlayer> right) =>
        left.OrderBy(player => player.Id).SequenceEqual(right.OrderBy(player => player.Id));

    private static async Task<(SafetySnapshotManifest? Manifest, IReadOnlyList<string> Errors)> ValidateSnapshotSourceAsync(
        string snapshotRoot,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath)) return (null, ["Manifeste du snapshot source absent."]);
        SafetySnapshotManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<SafetySnapshotManifest>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            return (null, [$"Manifeste du snapshot source invalide : {exception.Message}"]);
        }
        if (manifest is null || manifest.SchemaVersion != 1) return (null, ["Version de manifeste source non prise en charge."]);

        var wgsRoot = Path.Combine(snapshotRoot, "wgs");
        var errors = new List<string>();
        foreach (var entry in manifest.Files)
        {
            var path = ResolveContainedPath(wgsRoot, entry.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Fichier source absent : {entry.RelativePath}");
                continue;
            }
            var hash = await FileSafety.ComputeSha256Async(path, cancellationToken);
            if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) errors.Add($"Hash source invalide : {entry.RelativePath}");
        }
        return errors.Count == 0 ? (manifest, []) : (null, errors);
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!FileSafety.IsSameOrDescendant(path, root)) throw new InvalidDataException("Chemin de snapshot dangereux.");
        return path;
    }

    private static async Task CopyWithFlushAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static async Task RollBackFromSnapshotAsync(
        string snapshotDirectory,
        string testWorldName,
        string currentWgsRoot,
        string targetBlob,
        CancellationToken cancellationToken)
    {
        var snapshotWgs = Path.Combine(snapshotDirectory, "wgs");
        var (worlds, _) = await DiscoverWorldsAsync(snapshotWgs, cancellationToken);
        var world = worlds.Single(item => DisplayNamesEquivalent(item.DisplayName, testWorldName));
        var source = ResolveContainedPath(snapshotWgs, world.BlobRelativePath);
        var temporary = Path.Combine(Path.GetDirectoryName(targetBlob)!, $".gsh-rollback-{Guid.NewGuid():N}.tmp");
        await CopyWithFlushAsync(source, temporary, cancellationToken);
        File.Move(temporary, targetBlob, overwrite: true);
        _ = currentWgsRoot;
    }

    private static async Task<(PortableArtifactManifest Manifest, byte[] Payload)> ReadPortableArtifactAsync(
        string artifactPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("manifest.json absent de l'artefact.");
        var payloadEntry = archive.GetEntry("payload/world.save") ?? throw new InvalidDataException("payload/world.save absent de l'artefact.");
        PortableArtifactManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<PortableArtifactManifest>(manifestStream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Manifeste d'artefact illisible.");
        }
        await using var payloadStream = payloadEntry.Open();
        using var payloadBuffer = new MemoryStream();
        await payloadStream.CopyToAsync(payloadBuffer, cancellationToken);
        return (manifest, payloadBuffer.ToArray());
    }

    private async Task<PortableSaveArtifact> WritePortableArtifactAsync(
        PortableArtifactManifest manifest,
        byte[] payload,
        string outputRoot,
        string prefix,
        CancellationToken cancellationToken)
    {
        var fullOutputRoot = Path.GetFullPath(outputRoot);
        var detection = await DetectInstallationAsync(cancellationToken);
        if (detection.WgsRoot is not null &&
            (FileSafety.IsSameOrDescendant(fullOutputRoot, detection.WgsRoot) || FileSafety.IsSameOrDescendant(detection.WgsRoot, fullOutputRoot)))
        {
            throw new InvalidOperationException("Le dossier de sortie doit être séparé du stockage WGS.");
        }

        Directory.CreateDirectory(fullOutputRoot);
        var destination = Path.Combine(fullOutputRoot, $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{prefix}-{Guid.NewGuid():N}.gshsave");
        var temporary = destination + ".partial";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.NoCompression);
                    await using (var manifestStream = manifestEntry.Open())
                    {
                        await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
                    }
                    var payloadEntry = archive.CreateEntry("payload/world.save", CompressionLevel.NoCompression);
                    await using var payloadStream = payloadEntry.Open();
                    await payloadStream.WriteAsync(payload, cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination);
            return new PortableSaveArtifact(
                destination,
                await FileSafety.ComputeSha256Async(destination, cancellationToken),
                new FileInfo(destination).Length,
                manifest);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private async Task<(ManagedSlotBaselineManifest? Manifest, IReadOnlyList<string> Errors)> ValidateManagedSlotBaselineSourceAsync(
        string baselineDirectory,
        CancellationToken cancellationToken)
    {
        string root;
        try
        {
            root = Path.GetFullPath(baselineDirectory);
        }
        catch (Exception exception) when (IsPathValidationException(exception))
        {
            return (null, [$"Chemin de baseline invalide : {exception.Message}"]);
        }

        var manifestPath = Path.Combine(root, "managed-slot-baseline.json");
        if (!File.Exists(manifestPath)) return (null, ["Manifeste de baseline du slot permanent absent."]);
        ManagedSlotBaselineManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<ManagedSlotBaselineManifest>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return (null, [$"Manifeste de baseline du slot permanent invalide : {exception.Message}"]);
        }

        if (manifest is null ||
            manifest.SchemaVersion != 1 ||
            !string.Equals(manifest.AdapterId, AdapterId, StringComparison.Ordinal) ||
            !string.Equals(manifest.PackageFamilyName, _options.PackageFamilyName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(manifest.SnapshotId) ||
            manifest.Target is null ||
            manifest.Files is null ||
            manifest.ProtectedWorlds is null)
        {
            return (null, ["Baseline du slot permanent absente, incompatible ou non reconnue."]);
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(manifest.Target.LogicalName) ||
            string.IsNullOrWhiteSpace(manifest.Target.CurrentDisplayName) ||
            string.IsNullOrWhiteSpace(manifest.Target.DesiredDisplayName) ||
            string.IsNullOrWhiteSpace(manifest.Target.BeforePayloadSha256))
        {
            errors.Add("La cible de la baseline du slot permanent est incomplète.");
        }
        if (manifest.Files.Any(file =>
                file is null ||
                string.IsNullOrWhiteSpace(file.RelativePath) ||
                string.IsNullOrWhiteSpace(file.Sha256) ||
                file.Length < 0))
        {
            return (null, ["La liste de fichiers de la baseline contient une entrée invalide."]);
        }
        if (manifest.ProtectedWorlds.Any(world =>
                world is null ||
                string.IsNullOrWhiteSpace(world.LogicalName) ||
                string.IsNullOrWhiteSpace(world.DisplayName) ||
                string.IsNullOrWhiteSpace(world.PayloadSha256)))
        {
            return (null, ["La liste des mondes protégés contient une entrée invalide."]);
        }
        if (manifest.Files.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count)
        {
            errors.Add("La liste de fichiers de la baseline contient des chemins dupliqués.");
        }
        if (manifest.ProtectedWorlds.Select(world => world.LogicalName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.ProtectedWorlds.Count)
        {
            errors.Add("La liste des mondes protégés contient des noms logiques dupliqués.");
        }
        if (manifest.ProtectedWorlds.Any(world =>
                world.LogicalName.Equals(manifest.Target.LogicalName, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Le slot cible ne peut pas figurer parmi les mondes protégés.");
        }

        var snapshotWgs = Path.Combine(root, "wgs");
        if (!Directory.Exists(snapshotWgs))
        {
            errors.Add("Copie WGS de la baseline du slot permanent absente.");
            return (null, errors);
        }

        try
        {
            var expectedFiles = manifest.Files
                .Select(file => file.RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualFiles = EnumerateSafeFiles(snapshotWgs)
                .Select(path => FileSafety.GetSafeRelativePath(snapshotWgs, path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!expectedFiles.SetEquals(actualFiles))
            {
                errors.Add("La copie WGS ne correspond pas à la liste complète de fichiers du manifeste.");
            }

            foreach (var file in manifest.Files)
            {
                var path = ResolveContainedPath(snapshotWgs, file.RelativePath);
                if (!File.Exists(path))
                {
                    errors.Add($"Fichier de baseline absent : {file.RelativePath}");
                    continue;
                }
                var info = new FileInfo(path);
                var hash = await FileSafety.ComputeSha256Async(path, cancellationToken);
                if (info.Length != file.Length || !hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Fichier de baseline invalide : {file.RelativePath}");
                }
            }

            var (worlds, discoveryWarnings) = await DiscoverWorldsAsync(snapshotWgs, cancellationToken);
            if (discoveryWarnings.Count > 0)
            {
                errors.AddRange(discoveryWarnings.Select(warning => "Baseline WGS non interprétable : " + warning));
            }
            var expectedWorldNames = manifest.ProtectedWorlds
                .Select(world => world.LogicalName)
                .Append(manifest.Target.LogicalName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualWorldNames = worlds.Select(world => world.LogicalName).ToArray();
            if (actualWorldNames.Length != expectedWorldNames.Count ||
                actualWorldNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != actualWorldNames.Length ||
                !expectedWorldNames.SetEquals(actualWorldNames))
            {
                errors.Add("La topologie logique de la copie WGS ne correspond pas au manifeste.");
            }

            var targetWorlds = worlds
                .Where(world => world.LogicalName.Equals(manifest.Target.LogicalName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (targetWorlds.Length != 1)
            {
                errors.Add("La cible logique de la baseline est absente ou ambiguë dans la copie WGS.");
            }
            else
            {
                var target = targetWorlds[0];
                var targetPath = ResolveContainedPath(snapshotWgs, target.BlobRelativePath);
                var targetHash = await FileSafety.ComputeSha256Async(targetPath, cancellationToken);
                if (!target.DisplayName.Equals(manifest.Target.CurrentDisplayName, StringComparison.Ordinal) ||
                    target.WorldSeed != manifest.Target.WorldSeed ||
                    !targetHash.Equals(manifest.Target.BeforePayloadSha256, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("La cible de la copie WGS ne correspond pas au manifeste.");
                }
                var localPlayers = target.Players.Where(player => player.Id == 0).ToArray();
                if (localPlayers.Length != 1 || !localPlayers[0].IsHost || target.Players.Count(player => player.IsHost) != 1)
                {
                    errors.Add("La cible de la baseline n'a pas un joueur local ID 0 unique et hôte.");
                }
            }

            foreach (var protectedWorld in manifest.ProtectedWorlds)
            {
                var matches = worlds
                    .Where(world => world.LogicalName.Equals(protectedWorld.LogicalName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matches.Length != 1)
                {
                    errors.Add($"Monde protégé absent ou ambigu dans la baseline : {protectedWorld.LogicalName}");
                    continue;
                }
                var world = matches[0];
                var worldPath = ResolveContainedPath(snapshotWgs, world.BlobRelativePath);
                var worldHash = await FileSafety.ComputeSha256Async(worldPath, cancellationToken);
                if (!world.DisplayName.Equals(protectedWorld.DisplayName, StringComparison.Ordinal) ||
                    world.WorldSeed != protectedWorld.WorldSeed ||
                    !worldHash.Equals(protectedWorld.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Monde protégé invalide dans la baseline : {protectedWorld.LogicalName}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            errors.Add("Validation de la baseline impossible : " + exception.Message);
        }

        return errors.Count == 0 ? (manifest, []) : (null, errors);
    }

    private static List<string> VerifyManagedSlotReference(
        ManagedSlotBaselineManifest baseline,
        ManagedSlotReference slot)
    {
        var errors = new List<string>();
        if (!baseline.Target.LogicalName.Equals(slot.LogicalName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Le nom logique lié ne correspond pas à la cible de la baseline.");
        }
        if (!baseline.Target.CurrentDisplayName.Equals(slot.CurrentDisplayName, StringComparison.Ordinal))
        {
            errors.Add("Le nom affiché courant lié ne correspond pas à la baseline.");
        }
        if (!baseline.Target.DesiredDisplayName.Equals(slot.DesiredDisplayName, StringComparison.Ordinal))
        {
            errors.Add("Le nom affiché permanent désiré ne correspond pas à la baseline.");
        }
        return errors;
    }

    private static List<string> VerifyManagedSlotWorldTopology(
        ManagedSlotBaselineManifest baseline,
        LocalStorageInspection current)
    {
        var expected = baseline.ProtectedWorlds
            .Select(world => world.LogicalName)
            .Append(baseline.Target.LogicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = current.Worlds.Select(world => world.LogicalName).ToArray();
        return actual.Length == expected.Count &&
               actual.Distinct(StringComparer.OrdinalIgnoreCase).Count() == actual.Length &&
               expected.SetEquals(actual)
            ? []
            : ["La topologie logique WGS ne correspond plus à la baseline du slot permanent."];
    }

    private static List<string> VerifyProtectedWorlds(
        ManagedSlotBaselineManifest baseline,
        LocalStorageInspection current)
    {
        var errors = new List<string>();
        foreach (var protectedWorld in baseline.ProtectedWorlds)
        {
            var matches = current.Worlds
                .Where(world => world.LogicalName.Equals(protectedWorld.LogicalName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                errors.Add($"Monde protégé absent ou ambigu : {protectedWorld.LogicalName}");
                continue;
            }
            var currentWorld = matches[0];
            var hash = GetWorldPayloadHash(current, currentWorld);
            if (!hash.Equals(protectedWorld.PayloadSha256, StringComparison.OrdinalIgnoreCase) ||
                !currentWorld.DisplayName.Equals(protectedWorld.DisplayName, StringComparison.Ordinal) ||
                currentWorld.WorldSeed != protectedWorld.WorldSeed)
            {
                errors.Add($"Monde protégé modifié : {protectedWorld.LogicalName}");
            }
        }
        return errors;
    }

    private static List<string> VerifyImportedManagedTarget(
        ManagedSlotBaselineManifest baseline,
        PortableArtifactManifest artifact,
        LocalStorageInspection current,
        DiscoveredWorld target,
        string importedHash)
    {
        var errors = new List<string>();
        if (!target.LogicalName.Equals(baseline.Target.LogicalName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Le nom logique du slot cible a changé.");
        }
        if (!GetWorldPayloadHash(current, target).Equals(importedHash, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Le hash du slot cible ne correspond pas à l'artefact préparé.");
        }
        if (!target.DisplayName.Equals(baseline.Target.DesiredDisplayName, StringComparison.Ordinal) ||
            !target.DisplayName.Equals(artifact.DisplayName, StringComparison.Ordinal))
        {
            errors.Add("Le nom affiché du slot cible ne correspond pas au nom permanent désiré.");
        }
        if (target.WorldSeed != artifact.WorldSeed || !PlayersEquivalent(target.Players, artifact.Players))
        {
            errors.Add("La structure sémantique du slot cible ne correspond pas à l'artefact préparé.");
        }
        return errors;
    }

    private bool PathsOverlap(string left, string right)
    {
        var fullLeft = Path.GetFullPath(left);
        var fullRight = Path.GetFullPath(right);
        var finalPathResolver = _options.FinalPathResolver ?? FileSafety.ResolveDirectoryLinks;
        var resolvedLeft = finalPathResolver(fullLeft);
        var resolvedRight = finalPathResolver(fullRight);
        return FileSafety.IsSameOrDescendant(fullLeft, fullRight) ||
               FileSafety.IsSameOrDescendant(fullRight, fullLeft) ||
               FileSafety.IsSameOrDescendant(resolvedLeft, resolvedRight) ||
               FileSafety.IsSameOrDescendant(resolvedRight, resolvedLeft);
    }

    private static bool IsPathValidationException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException;

    private static async Task<(ImportBaselineManifest? Manifest, IReadOnlyList<string> Errors)> ValidateImportBaselineSourceAsync(
        string baselineDirectory,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(baselineDirectory);
        var manifestPath = Path.Combine(root, "import-baseline.json");
        if (!File.Exists(manifestPath)) return (null, ["Manifeste de baseline absent."]);
        ImportBaselineManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<ImportBaselineManifest>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            return (null, [$"Manifeste de baseline invalide : {exception.Message}"]);
        }
        if (manifest is null || manifest.SchemaVersion != 1 || !manifest.AdapterId.Equals(AdapterId, StringComparison.Ordinal))
        {
            return (null, ["Baseline absente, incompatible ou non reconnue."]);
        }

        var snapshotWgs = Path.Combine(root, "wgs");
        var errors = new List<string>();
        foreach (var file in manifest.Files)
        {
            var path = ResolveContainedPath(snapshotWgs, file.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Fichier de baseline absent : {file.RelativePath}");
                continue;
            }
            var hash = await FileSafety.ComputeSha256Async(path, cancellationToken);
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) errors.Add($"Hash de baseline invalide : {file.RelativePath}");
        }
        return errors.Count == 0 ? (manifest, []) : (null, errors);
    }

    private static List<string> VerifyProtectedWorlds(ImportBaselineManifest baseline, LocalStorageInspection current)
    {
        var errors = new List<string>();
        foreach (var protectedWorld in baseline.ProtectedWorlds)
        {
            var currentWorld = current.Worlds.SingleOrDefault(world => world.LogicalName.Equals(protectedWorld.LogicalName, StringComparison.OrdinalIgnoreCase));
            if (currentWorld is null)
            {
                errors.Add($"Monde protégé absent : {protectedWorld.LogicalName}");
                continue;
            }
            var hash = GetWorldPayloadHash(current, currentWorld);
            if (!hash.Equals(protectedWorld.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Monde protégé modifié : {protectedWorld.LogicalName}");
            }
        }
        return errors;
    }

    private static string GetWorldPayloadHash(LocalStorageInspection inspection, DiscoveredWorld world) =>
        inspection.Files.Single(file => file.RelativePath.Equals(world.BlobRelativePath, StringComparison.OrdinalIgnoreCase)).Sha256;

    private static int? ParseStandardIndex(string logicalName)
    {
        const string prefix = "Standard-";
        const string suffix = ".json";
        if (!logicalName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !logicalName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var number = logicalName[prefix.Length..^suffix.Length];
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
    }

    private static bool DisplayNamesEquivalent(string left, string right) =>
        NormalizeDisplayName(left).Equals(NormalizeDisplayName(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDisplayName(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormC);
        var start = 0;
        var end = normalized.Length;
        while (start < end && IsBoundaryIgnorable(normalized, start)) start += char.IsSurrogatePair(normalized, start) ? 2 : 1;
        while (end > start)
        {
            var index = end - 1;
            if (index > start && char.IsLowSurrogate(normalized[index]) && char.IsHighSurrogate(normalized[index - 1])) index--;
            if (!IsBoundaryIgnorable(normalized, index)) break;
            end = index;
        }
        return normalized[start..end];
    }

    private static bool IsBoundaryIgnorable(string value, int index)
    {
        var rune = char.ConvertToUtf32(value, index);
        var category = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(rune), 0);
        return category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Format || char.IsWhiteSpace(value, index);
    }

    private static async Task WriteBytesWithFlushAsync(byte[] payload, string destination, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await output.WriteAsync(payload, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static async Task RollBackLogicalWorldFromSnapshotAsync(
        string snapshotDirectory,
        string logicalName,
        string targetBlob,
        CancellationToken cancellationToken)
    {
        var snapshotWgs = Path.Combine(snapshotDirectory, "wgs");
        var (worlds, _) = await DiscoverWorldsAsync(snapshotWgs, cancellationToken);
        var world = worlds.SingleOrDefault(item => item.LogicalName.Equals(logicalName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Le monde {logicalName} est absent du snapshot de rollback.");
        var source = ResolveContainedPath(snapshotWgs, world.BlobRelativePath);
        var temporary = Path.Combine(Path.GetDirectoryName(targetBlob)!, $".gsh-rollback-{Guid.NewGuid():N}.tmp");
        await CopyWithFlushAsync(source, temporary, cancellationToken);
        File.Move(temporary, targetBlob, overwrite: true);
        var expectedHash = await FileSafety.ComputeSha256Async(source, cancellationToken);
        var actualHash = await FileSafety.ComputeSha256Async(targetBlob, cancellationToken);
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Le rollback n'a pas restauré le hash attendu.");
    }

    private static ManagedSlotBaselineResult ManagedSlotBaselineFailed(string error) => new(false, null, null, [error]);
    private static ImportBaselineResult ImportBaselineFailed(string error) => new(false, null, null, [error]);
    private static PortableImportResult ImportFailed(string error) => new(false, null, null, null, null, null, [error]);
    private static PortableImportResult ImportFailed(IReadOnlyList<string> errors) => new(false, null, null, null, null, null, errors);

    private static bool Equivalent(IReadOnlyList<DiagnosticFile> left, IReadOnlyList<DiagnosticFile> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.RelativePath.Equals(pair.Second.RelativePath, StringComparison.OrdinalIgnoreCase) &&
            pair.First.Length == pair.Second.Length &&
            pair.First.Sha256.Equals(pair.Second.Sha256, StringComparison.OrdinalIgnoreCase));

    private static SnapshotResult Failed(string error) => new(false, null, null, [error]);
    private static TestWorldRestoreResult RestoreFailed(string error) => new(false, null, null, null, null, [error]);
    private static TestWorldRestoreResult RestoreFailed(IReadOnlyList<string> errors) => new(false, null, null, null, null, errors);

    private static Task<T> NotValidated<T>() => Task.FromException<T>(
        new NotSupportedException("Fonction désactivée : le jalon expérimental WGS/transfert d'hôte n'est pas validé."));

    private sealed record LogicalFileState(string Sha256, long Length);
}
