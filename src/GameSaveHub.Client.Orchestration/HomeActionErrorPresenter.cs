namespace GameSaveHub.Client.Orchestration;

public static class HomeActionErrorPresenter
{
    public static string Present(string? code) => code switch
    {
        "active_transfer_exists" => "Une autre préparation est déjà en cours sur ce PC.",
        "client_update_required" => "Une mise à jour de GameSave Hub est nécessaire avant de prendre la main.",
        "player_required" or "player_not_found" or "player_ambiguous" =>
            "Votre pseudo ne correspond pas encore à un joueur de cette sauvegarde. Ouvrez l'assistance pour le vérifier.",
        _ => "L'action n'a pas pu aboutir. Vous pouvez réessayer ou ouvrir le diagnostic."
    };
}
