namespace GameSaveHub.Contracts;

public sealed record AcquisitionPlayerIdentityResult(
    bool Success,
    string Code,
    string? CanonicalPlayerName,
    string Message);

public static class AcquisitionPlayerIdentityResolver
{
    public static AcquisitionPlayerIdentityResult Resolve(
        string? requestedPlayerName,
        IReadOnlyList<WorldPreviewPlayerResponse> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (string.IsNullOrWhiteSpace(requestedPlayerName))
            return new(true, "legacy_player_absent", null, "Ancien client sans identité de joueur.");

        var compatibility = PlayerCompatibilityRules.Evaluate(requestedPlayerName, players);
        if (compatibility.Compatible)
            return new(true, "player_compatible", compatibility.MatchedPlayerName, compatibility.Message);

        var code = compatibility.Outcome switch
        {
            PlayerCompatibilityOutcome.PlayerNameMissing => "player_name_missing",
            PlayerCompatibilityOutcome.PlayerNotFound => "player_not_found",
            PlayerCompatibilityOutcome.PlayerAmbiguous => "player_ambiguous",
            _ => "player_incompatible"
        };
        return new(false, code, null, compatibility.Message);
    }
}
