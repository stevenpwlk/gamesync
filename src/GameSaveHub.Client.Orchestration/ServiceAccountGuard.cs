namespace GameSaveHub.Client.Orchestration;

/// <summary>
/// Extrait de la vérification déjà faite par <c>INSTALL-GAMESAVEHUB-CLIENT.ps1</c> :
/// le compte joueur enregistré ne peut jamais être LocalSystem/LocalService/NetworkService.
/// </summary>
public static class ServiceAccountGuard
{
    private static readonly HashSet<string> ReservedSids = ["S-1-5-18", "S-1-5-19", "S-1-5-20"];

    public static bool IsReservedAccount(string sid) => ReservedSids.Contains(sid);
}
