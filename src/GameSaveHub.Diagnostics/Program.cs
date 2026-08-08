using System.Text.Json;
using GameSaveHub.Adapters.PlanetCrafter.GamePass;
using GameSaveHub.Contracts;

return await DiagnosticApplication.RunAsync(args);

internal static class DiagnosticApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var adapter = new PlanetCrafterGamePassAdapter();
            return args[0].ToLowerInvariant() switch
            {
                "inventory" => await InventoryAsync(adapter, args[1..]),
                "snapshot" => await SnapshotAsync(adapter, args[1..]),
                "export-world" => await ExportWorldAsync(adapter, args[1..]),
                "validate-artifact" => await ValidateArtifactAsync(adapter, args[1..]),
                "prepare-host" => await PrepareHostAsync(adapter, args[1..]),
                "import-baseline" => await ImportBaselineAsync(adapter, args[1..]),
                "import-artifact" => await ImportArtifactAsync(adapter, args[1..]),
                "compare" => await CompareAsync(adapter, args[1..]),
                "validate-snapshot" => await ValidateSnapshotAsync(args[1..]),
                "restore-test-world" => await RestoreTestWorldAsync(adapter, args[1..]),
                "safety-status" => await SafetyStatusAsync(adapter, args[1..]),
                "capabilities" => PrintJson(adapter.Capabilities),
                _ => UnknownCommand(args[0])
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Opération annulée.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Erreur: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> InventoryAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        var output = ReadOption(args, "--json");
        EnsureOnlyOptions(args, "--json");
        var inspection = await adapter.InspectLocalStorageAsync();
        var installation = await adapter.DetectInstallationAsync();

        Console.WriteLine("GameSave Hub — inventaire WGS en lecture seule");
        Console.WriteLine($"Package : {inspection.PackageFamilyName}");
        Console.WriteLine($"Version : {installation.InstalledVersion ?? "inconnue"}");
        Console.WriteLine($"Installation : {installation.InstallLocation ?? "non résolue"}");
        Console.WriteLine($"Jeu actif : {(inspection.GameRunning ? "oui" : "non")}");
        Console.WriteLine($"Capture cohérente : {(inspection.Stable ? "oui" : "non")}");
        Console.WriteLine($"Fichiers : {inspection.Files.Count}, {inspection.Files.Sum(file => file.Length):N0} octets");
        foreach (var world in inspection.Worlds)
        {
            Console.WriteLine($"Monde : {world.DisplayName} ({world.LogicalName}, seed {world.WorldSeed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "inconnue"})");
            foreach (var player in world.Players)
            {
                Console.WriteLine($"  Joueur : {player.Name} (id {player.Id}, hôte {(player.IsHost ? "oui" : "non")}, inventaire {player.InventoryId}, équipement {player.EquipmentId})");
            }
        }
        foreach (var file in inspection.Files)
        {
            Console.WriteLine($"  {file.Role,-18} {file.Length,12:N0}  {file.Sha256[..12]}…  {file.RelativePath}");
        }
        foreach (var warning in inspection.Warnings)
        {
            Console.WriteLine($"AVERTISSEMENT : {warning}");
        }

        if (output is not null)
        {
            await WriteJsonAtomicallyAsync(output, inspection);
            Console.WriteLine($"Rapport JSON : {Path.GetFullPath(output)}");
        }
        return inspection.Stable ? 0 : 2;
    }

    private static async Task<int> SnapshotAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        var output = ReadOption(args, "--output") ?? throw new ArgumentException("L'option --output est obligatoire.");
        var testWorld = ReadOption(args, "--test-world") ?? throw new ArgumentException("L'option --test-world est obligatoire.");
        var acknowledged = args.Contains("--acknowledge-test-world", StringComparer.OrdinalIgnoreCase);
        EnsureOnlyOptions(args, "--output", "--test-world", "--acknowledge-test-world");

        var result = await adapter.CreateSafetySnapshotAsync(output, acknowledged ? testWorld : null);
        if (!result.Success)
        {
            foreach (var error in result.Errors) Console.Error.WriteLine($"REFUS : {error}");
            return 3;
        }

        Console.WriteLine("Capture cohérente créée. Aucun fichier WGS source n'a été modifié.");
        Console.WriteLine(result.SnapshotDirectory);
        return 0;
    }

    private static async Task<int> ExportWorldAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        var world = ReadOption(args, "--world") ?? throw new ArgumentException("L'option --world est obligatoire.");
        var output = ReadOption(args, "--output") ?? throw new ArgumentException("L'option --output est obligatoire.");
        EnsureOnlyOptions(args, "--world", "--output");
        var artifact = await adapter.ExportPortableArtifactAsync(world, output);
        Console.WriteLine($"Artefact créé : {artifact.Path}");
        Console.WriteLine($"Monde logique : {artifact.Manifest?.LogicalName}");
        Console.WriteLine($"Taille : {artifact.Length:N0} octets");
        Console.WriteLine($"SHA-256 : {artifact.Sha256}");
        return 0;
    }

    private static async Task<int> ValidateArtifactAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Usage : validate-artifact <fichier.gshsave>");
        var path = Path.GetFullPath(args[0]);
        var placeholder = new PortableSaveArtifact(path, string.Empty, File.Exists(path) ? new FileInfo(path).Length : 0, null);
        var validation = await adapter.ValidateArtifactAsync(placeholder);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors) Console.Error.WriteLine($"INVALIDE : {error}");
            return 6;
        }
        Console.WriteLine("Artefact valide : enveloppe, taille, hash et contenu sémantique vérifiés.");
        return 0;
    }

    private static async Task<int> PrepareHostAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        var artifactPath = ReadOption(args, "--artifact") ?? throw new ArgumentException("L'option --artifact est obligatoire.");
        var player = ReadOption(args, "--player") ?? throw new ArgumentException("L'option --player est obligatoire.");
        var output = ReadOption(args, "--output") ?? throw new ArgumentException("L'option --output est obligatoire.");
        EnsureOnlyOptions(args, "--artifact", "--player", "--output");

        var artifact = await LoadArtifactReferenceAsync(artifactPath);
        var result = await adapter.PrepareForHostAsync(artifact, player, output);
        if (!result.Success)
        {
            Console.Error.WriteLine($"REFUS : {result.Outcome}");
            foreach (var error in result.Errors) Console.Error.WriteLine($"  {error}");
            return 7;
        }

        Console.WriteLine(result.Changed ? "Artefact préparé pour un nouvel hôte." : "Le joueur cible est déjà le joueur ID 0 hôte; artefact recopié sans transformation sémantique.");
        Console.WriteLine($"Joueur cible : {result.TargetPlayerName}");
        Console.WriteLine($"ID cible avant préparation : {result.TargetPlayerOriginalId}");
        Console.WriteLine($"Artefact préparé : {result.PreparedArtifact!.Path}");
        Console.WriteLine($"SHA-256 : {result.PreparedArtifact.Sha256}");
        return 0;
    }

    private static async Task<int> ImportBaselineAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        var output = ReadOption(args, "--output") ?? throw new ArgumentException("L'option --output est obligatoire.");
        EnsureOnlyOptions(args, "--output");
        var result = await adapter.CreateImportBaselineAsync(output);
        if (!result.Success)
        {
            foreach (var error in result.Errors) Console.Error.WriteLine($"REFUS : {error}");
            return 8;
        }

        Console.WriteLine("Baseline d'import créée. Tous les mondes actuels sont maintenant protégés par hash.");
        Console.WriteLine($"Dossier : {result.BaselineDirectory}");
        Console.WriteLine($"Mondes protégés : {result.Manifest!.ProtectedWorlds.Count}");
        Console.WriteLine($"Plus grand Standard-X actuel : {result.Manifest.MaximumStandardIndex}");
        Console.WriteLine("Créez maintenant UN SEUL nouveau monde placeholder dans Planet Crafter, sauvegardez et fermez le jeu avant l'import.");
        return 0;
    }

    private static async Task<int> ImportArtifactAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        var artifactPath = ReadOption(args, "--artifact") ?? throw new ArgumentException("L'option --artifact est obligatoire.");
        var baseline = ReadOption(args, "--baseline") ?? throw new ArgumentException("L'option --baseline est obligatoire.");
        var player = ReadOption(args, "--player") ?? throw new ArgumentException("L'option --player est obligatoire.");
        var placeholder = ReadOption(args, "--placeholder") ?? throw new ArgumentException("L'option --placeholder est obligatoire.");
        var backupOutput = ReadOption(args, "--backup-output") ?? throw new ArgumentException("L'option --backup-output est obligatoire.");
        var acknowledged = args.Contains("--acknowledge-pilot-import", StringComparer.OrdinalIgnoreCase);
        EnsureOnlyOptions(args, "--artifact", "--baseline", "--player", "--placeholder", "--backup-output", "--acknowledge-pilot-import");
        if (!acknowledged)
        {
            throw new ArgumentException("L'option --acknowledge-pilot-import est obligatoire pour toute écriture WGS pilote.");
        }

        var artifact = await LoadArtifactReferenceAsync(artifactPath);
        var result = await adapter.ImportPortableArtifactAsync(artifact, baseline, player, placeholder, backupOutput);
        if (!result.Success)
        {
            foreach (var error in result.Errors) Console.Error.WriteLine($"REFUS/ECHEC : {error}");
            return 9;
        }

        Console.WriteLine("Import pilote ciblé réussi.");
        Console.WriteLine($"Slot logique : {result.TargetLogicalName}");
        Console.WriteLine($"Monde importé : {result.TargetDisplayName}");
        Console.WriteLine($"Hash placeholder précédent : {result.PreviousPayloadSha256}");
        Console.WriteLine($"Hash importé : {result.ImportedPayloadSha256}");
        Console.WriteLine($"Snapshot pré-import : {result.PreImportSnapshotDirectory}");
        Console.WriteLine("Le feature gate serveur reste fermé; observez Xbox Cloud et validez le monde avant toute promotion produit.");
        return 0;
    }

    private static async Task<int> CompareAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage : compare <manifest-avant> <manifest-après>");
        var before = await ReadManifestAsync(args[0]);
        var after = await ReadManifestAsync(args[1]);
        var difference = SnapshotComparer.Compare(before, after);
        Console.WriteLine("Comparaison physique WGS (les identifiants de blobs et générations container.* peuvent tourner à chaque sauvegarde).");
        PrintSection("Ajoutés", difference.Added);
        PrintSection("Supprimés", difference.Removed);
        PrintSection("Modifiés", difference.Changed);
        Console.WriteLine($"Inchangés : {difference.Unchanged.Count}");
        var beforeRoot = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
        var afterRoot = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
        var logical = await adapter.CompareSnapshotsLogicallyAsync(beforeRoot, afterRoot);
        Console.WriteLine("Comparaison logique WGS");
        foreach (var file in logical.Files)
        {
            Console.WriteLine($"  {file.Status,-9} {file.LogicalName} ({file.BeforeLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} → {file.AfterLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} octets)");
        }
        return 0;
    }

    private static async Task<int> ValidateSnapshotAsync(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Usage : validate-snapshot <dossier-capture>");
        var root = Path.GetFullPath(args[0]);
        var manifest = await ReadManifestAsync(Path.Combine(root, "snapshot-manifest.json"));
        var errors = new List<string>();
        var snapshotWgsRoot = Path.Combine(root, "wgs");
        foreach (var entry in manifest.Files)
        {
            var path = Path.GetFullPath(Path.Combine(snapshotWgsRoot, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!GameSaveHub.Core.FileSafety.IsSameOrDescendant(path, snapshotWgsRoot))
            {
                errors.Add($"Chemin dangereux : {entry.RelativePath}");
                continue;
            }
            if (!File.Exists(path))
            {
                errors.Add($"Absent : {entry.RelativePath}");
                continue;
            }
            var hash = await GameSaveHub.Core.FileSafety.ComputeSha256Async(path);
            if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) errors.Add($"Hash invalide : {entry.RelativePath}");
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors) Console.Error.WriteLine(error);
            return 4;
        }
        Console.WriteLine($"Capture valide : {manifest.Files.Count} fichiers vérifiés.");
        return 0;
    }

    private static async Task<int> RestoreTestWorldAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        var source = ReadOption(args, "--from-snapshot") ?? throw new ArgumentException("L'option --from-snapshot est obligatoire.");
        var world = ReadOption(args, "--test-world") ?? throw new ArgumentException("L'option --test-world est obligatoire.");
        var backupOutput = ReadOption(args, "--backup-output") ?? throw new ArgumentException("L'option --backup-output est obligatoire.");
        var testAcknowledged = args.Contains("--acknowledge-test-world", StringComparer.OrdinalIgnoreCase);
        var offlineAcknowledged = args.Contains("--acknowledge-offline", StringComparer.OrdinalIgnoreCase);
        EnsureOnlyOptions(args, "--from-snapshot", "--test-world", "--backup-output", "--acknowledge-test-world", "--acknowledge-offline");
        if (!testAcknowledged) throw new ArgumentException("L'option --acknowledge-test-world est obligatoire.");

        var result = await adapter.RestoreTestWorldFromSnapshotAsync(source, world, backupOutput, offlineAcknowledged);
        if (!result.Success)
        {
            foreach (var error in result.Errors) Console.Error.WriteLine($"REFUS : {error}");
            return 5;
        }

        Console.WriteLine($"Restauration ciblée réussie : {result.LogicalName}");
        Console.WriteLine($"Hash précédent : {result.PreviousSha256}");
        Console.WriteLine($"Hash restauré  : {result.RestoredSha256}");
        Console.WriteLine($"Snapshot automatique pré-restauration : {result.PreRestoreSnapshotDirectory}");
        Console.WriteLine("Restez hors ligne jusqu'à la capture et la validation après lancement du jeu.");
        return 0;
    }

    private static async Task<int> SafetyStatusAsync(PlanetCrafterGamePassAdapter adapter, string[] args)
    {
        if (args.Length != 0) throw new ArgumentException("La commande safety-status n'accepte aucune option.");
        var status = await adapter.GetDiagnosticSafetyStatusAsync();
        Console.WriteLine($"Jeu actif : {(status.GameRunning ? "oui" : "non")}");
        Console.WriteLine($"Route réseau active : {(status.ActiveNetworkRoute ? "oui" : "non")}");
        Console.WriteLine($"Prêt pour l'essai hors ligne : {(status.SafeForOfflineTest ? "oui" : "non")}");
        return status.SafeForOfflineTest ? 0 : 2;
    }

    private static async Task<PortableSaveArtifact> LoadArtifactReferenceAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Artefact introuvable.", fullPath);
        return new PortableSaveArtifact(
            fullPath,
            await GameSaveHub.Core.FileSafety.ComputeSha256Async(fullPath),
            new FileInfo(fullPath).Length,
            null);
    }

    private static async Task<SafetySnapshotManifest> ReadManifestAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SafetySnapshotManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException("Manifeste JSON vide ou invalide.");
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath)) throw new IOException("Le fichier de rapport existe déjà; aucun écrasement automatique n'est autorisé.");
        var temporary = fullPath + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, fullPath);
    }

    private static string? ReadOption(string[] args, string option)
    {
        var index = Array.FindIndex(args, value => value.Equals(option, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        if (index == args.Length - 1 || args[index + 1].StartsWith('-')) throw new ArgumentException($"Valeur manquante pour {option}.");
        return args[index + 1];
    }

    private static void EnsureOnlyOptions(string[] args, params string[] allowed)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith('-')) continue;
            if (!allowed.Contains(args[index], StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"Option inconnue : {args[index]}");
            if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--json", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--test-world", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--world", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--from-snapshot", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--backup-output", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--artifact", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--player", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--baseline", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--placeholder", StringComparison.OrdinalIgnoreCase)) index++;
        }
    }

    private static int PrintJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Commande inconnue : {command}");
        PrintHelp();
        return 64;
    }

    private static void PrintSection(string title, IReadOnlyList<string> values)
    {
        Console.WriteLine($"{title} ({values.Count})");
        foreach (var value in values) Console.WriteLine($"  {value}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            GameSave Hub Diagnostics — Phase 0

            Commandes sûres :
              inventory [--json <nouveau-fichier>]
              capabilities
              safety-status
              export-world --world <nom-affiché> --output <dossier>
              validate-artifact <fichier.gshsave>
              compare <manifest-avant> <manifest-après>
              validate-snapshot <dossier-capture>

            Capture de sécurité (jeu fermé, monde jetable uniquement) :
              snapshot --output <dossier> --test-world <nom-affiché> --acknowledge-test-world

            Restauration diagnostique ciblée (jeu fermé et PC réellement hors ligne) :
              restore-test-world --from-snapshot <capture> --test-world <nom> --backup-output <dossier> --acknowledge-test-world --acknowledge-offline

            Pilote cross-PC (feature gate serveur toujours fermé) :
              prepare-host --artifact <fichier.gshsave> --player <pseudo-existant> --output <dossier>
              import-baseline --output <dossier>
              import-artifact --artifact <prepare.gshsave> --baseline <dossier-baseline> --player <pseudo-existant> --placeholder <nom> --backup-output <dossier> --acknowledge-pilot-import

            prepare-host refuse tout pseudo absent ou ambigu. import-artifact ne peut cibler que l'unique nouveau Standard-X créé après import-baseline.
            """);
    }
}

internal static class SnapshotComparer
{
    public static SnapshotDifference Compare(SafetySnapshotManifest before, SafetySnapshotManifest after)
    {
        var left = before.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var right = after.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var added = right.Keys.Except(left.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var removed = left.Keys.Except(right.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var common = left.Keys.Intersect(right.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        var changed = common.Where(path => left[path].Length != right[path].Length || !left[path].Sha256.Equals(right[path].Sha256, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var unchanged = common.Except(changed, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return new SnapshotDifference(added, removed, changed, unchanged);
    }
}
