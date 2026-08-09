using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Orchestration;

public static class PrimaryWorldSelector
{
    public static PrimaryWorldSelection Select(IReadOnlyList<WorldCatalogItemResponse> worlds)
    {
        ArgumentNullException.ThrowIfNull(worlds);
        var candidates = worlds.Where(world => world.HasArtifact).ToArray();
        return candidates.Length switch
        {
            1 => new PrimaryWorldSelection(true, "primary_world_ready", candidates[0]),
            0 => new PrimaryWorldSelection(false, "primary_world_missing", null),
            _ => new PrimaryWorldSelection(false, "multiple_primary_worlds", null)
        };
    }
}
