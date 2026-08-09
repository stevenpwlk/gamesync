using GameSaveHub.Server.Infrastructure;

namespace GameSaveHub.Server.Admin;

public static class WorldReplaceCommandParser
{
    public static WorldReplacementRequest Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 5 ||
            !args[0].Equals("world", StringComparison.OrdinalIgnoreCase) ||
            !args[1].Equals("replace", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Syntaxe invalide pour world replace.");

        var worldId = Guid.Parse(args[2]);
        var artifactPath = Path.GetFullPath(args[3]);
        var expectedVersionId = Guid.Parse(args[4]);
        string? sourcePlayer = null;
        string? reason = null;
        var requiredPlayers = new List<string>();

        for (var index = 5; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
                throw new InvalidOperationException($"Valeur manquante pour l'option '{args[index]}'.");
            var value = args[index + 1].Trim();
            if (value.Length == 0)
                throw new InvalidOperationException($"Valeur vide pour l'option '{args[index]}'.");

            switch (args[index].ToLowerInvariant())
            {
                case "--source-player" when sourcePlayer is null:
                    sourcePlayer = value;
                    break;
                case "--require-player":
                    requiredPlayers.Add(value);
                    break;
                case "--reason" when reason is null:
                    reason = value;
                    break;
                default:
                    throw new InvalidOperationException($"Option inconnue ou dupliquée : {args[index]}.");
            }
        }

        if (sourcePlayer is null)
            throw new InvalidOperationException("L'option --source-player est requise.");
        if (reason is null)
            throw new InvalidOperationException("L'option --reason est requise.");

        return new WorldReplacementRequest(
            worldId,
            artifactPath,
            expectedVersionId,
            sourcePlayer,
            requiredPlayers,
            reason);
    }
}
