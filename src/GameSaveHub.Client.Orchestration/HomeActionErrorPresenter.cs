namespace GameSaveHub.Client.Orchestration;

public static class HomeActionErrorPresenter
{
    public static string Present(string? code) => code switch
    {
        "active_transfer_exists" or "active_session_exists" => "Une autre préparation est déjà en cours sur ce PC.",
        "client_update_required" => "Une mise à jour de GameSave Hub est nécessaire avant de prendre la main.",
        "player_required" or "player_not_found" or "player_ambiguous" =>
            "Votre pseudo ne correspond pas encore à un joueur de cette sauvegarde. Ouvrez l'assistance pour le vérifier.",
        "game_running" => "Fermez complètement The Planet Crafter avant de continuer.",
        "wgs_not_stable" => "La sauvegarde Xbox n'est pas encore stable. Réessayez dans quelques instants.",
        "managed_slot_already_bound" => "Un slot est déjà enregistré sur ce PC. Ouvrez l'assistance si l'état affiché semble incorrect.",
        "managed_slot_candidate_changed" or "managed_slot_bind_unavailable" or "managed_slot_binding_missing" or "managed_slot_requires_attention" =>
            "Le monde partagé de ce PC doit être vérifié avant de continuer. Ouvrez l'assistance pour les détails.",
        _ => "L'action n'a pas pu aboutir. Vous pouvez réessayer ou ouvrir le diagnostic."
    };
}
