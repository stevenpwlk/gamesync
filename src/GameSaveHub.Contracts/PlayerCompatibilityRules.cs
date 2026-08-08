using System.Text;

namespace GameSaveHub.Contracts;

public enum PlayerCompatibilityOutcome
{
    Compatible,
    PlayerNameMissing,
    PlayerNotFound,
    PlayerAmbiguous
}

public sealed record PlayerCompatibilityResult(
    bool Compatible,
    PlayerCompatibilityOutcome Outcome,
    string? MatchedPlayerName,
    int MatchCount,
    string Message);

public static class PlayerCompatibilityRules
{
    public static PlayerCompatibilityResult Evaluate(
        string? configuredPlayerName,
        IReadOnlyList<WorldPreviewPlayerResponse> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (string.IsNullOrWhiteSpace(configuredPlayerName))
        {
            return new PlayerCompatibilityResult(
                false,
                PlayerCompatibilityOutcome.PlayerNameMissing,
                null,
                0,
                "Aucun pseudo Planet Crafter n'est configuré pour ce PC.");
        }

        var requested = Normalize(configuredPlayerName);
        var matches = players
            .Where(player => Normalize(player.Name).Equals(requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            0 => new PlayerCompatibilityResult(
                false,
                PlayerCompatibilityOutcome.PlayerNotFound,
                null,
                0,
                $"Le joueur '{configuredPlayerName.Trim()}' n'existe pas dans cette sauvegarde. Le transfert est bloqué."),
            1 => new PlayerCompatibilityResult(
                true,
                PlayerCompatibilityOutcome.Compatible,
                matches[0].Name,
                1,
                $"Joueur compatible trouvé : {matches[0].Name}."),
            _ => new PlayerCompatibilityResult(
                false,
                PlayerCompatibilityOutcome.PlayerAmbiguous,
                null,
                matches.Length,
                $"Le pseudo '{configuredPlayerName.Trim()}' correspond à plusieurs joueurs. Le transfert est bloqué.")
        };
    }

    private static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormC);
}
