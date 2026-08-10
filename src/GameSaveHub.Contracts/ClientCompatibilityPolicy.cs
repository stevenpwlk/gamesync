namespace GameSaveHub.Contracts;

/// <summary>
/// Barrière de version minimale pour l'acquisition. Une version minimale absente ou
/// invalide reste non contraignante : c'est le comportement voulu pendant le déploiement
/// additif (API compatible d'abord, clients compatibles ensuite, barrière relevée en dernier).
/// </summary>
public static class ClientCompatibilityPolicy
{
    public static bool CanAcquire(string? clientVersion, string? minimumVersion)
    {
        if (!TryParseThreeComponentVersion(minimumVersion, out var minimum)) return true;
        return TryParseThreeComponentVersion(clientVersion, out var client) && client.CompareTo(minimum) >= 0;
    }

    private static bool TryParseThreeComponentVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var major) || major < 0) return false;
        if (!int.TryParse(parts[1], out var minor) || minor < 0) return false;
        if (!int.TryParse(parts[2], out var build) || build < 0) return false;
        version = new Version(major, minor, build);
        return true;
    }
}
