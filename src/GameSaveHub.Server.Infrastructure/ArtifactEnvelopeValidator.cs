using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameSaveHub.Adapters.PlanetCrafter.GamePass;

namespace GameSaveHub.Server.Infrastructure;

public static class ArtifactEnvelopeValidator
{
    private const int MaximumEntryCount = 2;
    private const int MaximumCompressionRatio = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task ValidateAsync(string path, long maximumBytes, CancellationToken cancellationToken = default)
    {
        _ = await ReadSummaryAsync(path, maximumBytes, cancellationToken);
    }

    public static async Task<ArtifactEnvelopeSummary> ReadSummaryAsync(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes)
            throw new InvalidOperationException("Enveloppe absente, vide ou surdimensionnée.");

        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count != MaximumEntryCount)
            throw new InvalidOperationException("L'enveloppe doit contenir exactement deux entrées.");

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Contains('\\', StringComparison.Ordinal) || entry.FullName.StartsWith('/') ||
                entry.FullName.Split('/').Any(segment => segment is "" or "." or ".."))
                throw new InvalidOperationException("Chemin dangereux dans l'enveloppe.");
            if (entry.Length > maximumBytes)
                throw new InvalidOperationException("Entrée surdimensionnée.");
            if (entry.CompressedLength == 0 ? entry.Length > 0 : entry.Length > entry.CompressedLength * MaximumCompressionRatio)
                throw new InvalidOperationException("Ratio de compression dangereux.");
        }

        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidOperationException("Manifeste absent.");
        var payloadEntry = archive.GetEntry("payload/world.save") ?? throw new InvalidOperationException("Payload absent.");
        ArtifactManifestEnvelope manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<ArtifactManifestEnvelope>(
                manifestStream,
                JsonOptions,
                cancellationToken)
                ?? throw new InvalidOperationException("Manifeste JSON invalide.");
        }

        if (manifest.SchemaVersion != 1 ||
            manifest.AdapterId != "planet-crafter-pc-gamepass" ||
            string.IsNullOrWhiteSpace(manifest.LogicalName) ||
            manifest.PayloadPath != payloadEntry.FullName ||
            manifest.PayloadLength != payloadEntry.Length ||
            string.IsNullOrWhiteSpace(manifest.PayloadSha256) ||
            !IsSha256(manifest.PayloadSha256))
        {
            throw new InvalidOperationException("Métadonnées du manifeste invalides.");
        }

        await using var payload = payloadEntry.Open();
        var actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(payload, cancellationToken));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash),
                Convert.FromHexString(manifest.PayloadSha256)))
        {
            throw new InvalidOperationException("Hash du payload invalide.");
        }

        await using var semanticPayload = payloadEntry.Open();
        using var reader = new StreamReader(
            semanticPayload,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: false);
        var text = await reader.ReadToEndAsync(cancellationToken);
        var parsed = PlanetCrafterWorldPayloadReader.Parse(
            manifest.LogicalName ?? string.Empty,
            text,
            manifest.PayloadPath);
        if (parsed is null ||
            !parsed.DisplayName.Equals(manifest.DisplayName, StringComparison.Ordinal) ||
            parsed.WorldSeed != manifest.WorldSeed ||
            !PlayersEquivalent(parsed.Players, manifest.Players ?? []))
        {
            throw new InvalidOperationException("Le contenu sémantique du monde ne correspond pas au manifeste.");
        }

        var players = (manifest.Players ?? [])
            .Select(player => new ArtifactEnvelopePlayerSummary(
                player.Id,
                player.Name ?? string.Empty,
                player.IsHost,
                player.InventoryId,
                player.EquipmentId))
            .ToArray();

        return new ArtifactEnvelopeSummary(
            manifest.SchemaVersion,
            manifest.AdapterId,
            manifest.DisplayName,
            manifest.WorldSeed,
            manifest.PayloadLength,
            manifest.PayloadSha256.ToLowerInvariant(),
            players);
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool PlayersEquivalent(
        IReadOnlyList<GameSaveHub.Contracts.DiscoveredPlayer> parsed,
        IReadOnlyList<ArtifactManifestPlayerEnvelope> manifest) =>
        parsed
            .OrderBy(player => player.Id)
            .Select(player => (player.Id, player.Name, player.IsHost, player.InventoryId, player.EquipmentId))
            .SequenceEqual(manifest
                .OrderBy(player => player.Id)
                .Select(player => (player.Id, player.Name ?? string.Empty, player.IsHost, player.InventoryId, player.EquipmentId)));

    private sealed record ArtifactManifestEnvelope(
        int SchemaVersion,
        string AdapterId,
        string? LogicalName,
        string PayloadPath,
        long PayloadLength,
        string PayloadSha256,
        string? DisplayName,
        long? WorldSeed,
        IReadOnlyList<ArtifactManifestPlayerEnvelope>? Players);

    private sealed record ArtifactManifestPlayerEnvelope(
        int Id,
        string? Name,
        bool IsHost,
        int InventoryId,
        int EquipmentId);
}

public sealed record ArtifactEnvelopeSummary(
    int SchemaVersion,
    string AdapterId,
    string? DisplayName,
    long? WorldSeed,
    long PayloadLength,
    string PayloadSha256,
    IReadOnlyList<ArtifactEnvelopePlayerSummary> Players);

public sealed record ArtifactEnvelopePlayerSummary(
    int Id,
    string Name,
    bool IsHost,
    int InventoryId,
    int EquipmentId);
