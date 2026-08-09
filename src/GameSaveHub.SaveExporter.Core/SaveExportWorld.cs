namespace GameSaveHub.SaveExporter.Core;

public sealed record SaveExportWorld(
    string LogicalName,
    string DisplayName,
    DateTimeOffset? LastModifiedAtUtc,
    string? Mode,
    IReadOnlyList<SaveExportPlayer> Players);

public sealed record SaveExportPlayer(string Name, bool IsHost)
{
    public string RoleLabel => IsHost ? "Hôte" : "Joueur";
}
