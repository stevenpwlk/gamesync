using System.Text;

namespace GameSaveHub.Server.Infrastructure;

public static class ArtifactTopologyValidator
{
    public static void Validate(
        ArtifactEnvelopeSummary summary,
        IEnumerable<string> requiredPlayerNames)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(requiredPlayerNames);

        var players = summary.Players;
        if (players.Count == 0)
            throw new InvalidOperationException("La sauvegarde ne contient aucun joueur.");
        if (players.Any(player => string.IsNullOrWhiteSpace(player.Name)))
            throw new InvalidOperationException("Chaque joueur doit posséder un pseudo.");
        if (players.Select(player => player.Id).Distinct().Count() != players.Count ||
            players.Count(player => player.Id == 0) != 1)
            throw new InvalidOperationException("Les identifiants joueur doivent être uniques et contenir exactement un ID 0.");

        var hosts = players.Where(player => player.IsHost).ToArray();
        if (hosts.Length != 1 || hosts[0].Id != 0)
            throw new InvalidOperationException("La sauvegarde doit contenir exactement un hôte, portant l'ID 0.");
        if (players.Select(player => player.InventoryId).Distinct().Count() != players.Count)
            throw new InvalidOperationException("Les identifiants d'inventaire joueur doivent être uniques.");
        if (players.Select(player => player.EquipmentId).Distinct().Count() != players.Count)
            throw new InvalidOperationException("Les identifiants d'équipement joueur doivent être uniques.");

        foreach (var requiredName in requiredPlayerNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(NormalizedPlayerNameComparer.Instance))
        {
            var normalized = Normalize(requiredName);
            var count = players.Count(player =>
                Normalize(player.Name).Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (count != 1)
                throw new InvalidOperationException(
                    $"Le joueur requis '{requiredName.Trim()}' doit être présent exactement une fois (trouvé : {count}).");
        }
    }

    private static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormC);

    private sealed class NormalizedPlayerNameComparer : IEqualityComparer<string>
    {
        public static NormalizedPlayerNameComparer Instance { get; } = new();
        public bool Equals(string? x, string? y) =>
            x is not null && y is not null &&
            Normalize(x).Equals(Normalize(y), StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(string value) => StringComparer.OrdinalIgnoreCase.GetHashCode(Normalize(value));
    }
}
