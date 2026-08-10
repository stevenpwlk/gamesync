namespace GameSaveHub.Client.Orchestration;

public static class HomeStatePresenter
{
    public static HomeViewState Present(HomeContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.IsEnrolled)
            return View(HomeVisualState.Onboarding, "Associons ce PC", "Saisissez votre invitation et votre pseudo pour accéder au monde partagé.", 1);
        if (!context.ServerHealthy)
            return View(HomeVisualState.Unavailable, "Le monde est momentanément inaccessible", "La connexion sera réessayée automatiquement.", 1, indeterminate: true);
        if (!string.IsNullOrWhiteSpace(context.SafetyStopCode))
            return View(
                HomeVisualState.SafetyStop,
                "Une vérification est nécessaire",
                context.SafetyStopCode is "primary_world_missing" or "multiple_primary_worlds"
                    ? "Le monde principal ne peut pas être déterminé sans ambiguïté."
                    : "Les états local et serveur ne concordent pas ; aucune action automatique ne sera tentée.",
                1,
                HomePrimaryAction.OpenDiagnostics,
                "Ouvrir l'assistance");

        if (context.LocalSession is TransferSession local && TransferStageRules.HoldsLocalLock(local.Stage))
            return PresentLocal(local, context.GameRunning);

        if (context.WorldStatus?.ActiveSession is { } remote)
            return PresentRemote(remote.PlayerName, remote.State, context.GameRunning);

        if (context.GameRunning)
            return View(HomeVisualState.OffHub, "Le jeu est lancé hors de GameSave Hub", "Fermez The Planet Crafter avant toute prise en main.", 1);

        if (!context.WgsAvailable)
            return View(
                HomeVisualState.ReadyToPlay,
                "Initialisons la sauvegarde Xbox",
                "Lancez The Planet Crafter une première fois afin que Xbox crée son espace de sauvegarde.",
                1,
                HomePrimaryAction.LaunchGame,
                "Lancer The Planet Crafter");

        if (!context.WgsStable)
            return View(HomeVisualState.SafetyStop, "La sauvegarde Xbox se synchronise", "Patientez quelques instants : la prise en main sera proposée dès que les fichiers seront stables.", 1, indeterminate: true);

        return PresentSlotStatus(context.ManagedSlotStatus);
    }

    private static HomeViewState PresentSlotStatus(ManagedSlotStatus status) => status switch
    {
        ManagedSlotStatus.Ready => View(
            HomeVisualState.Ready,
            "Le monde est prêt",
            "Vous pouvez préparer la dernière version pour héberger la partie.",
            1,
            HomePrimaryAction.StartTransfer,
            "Prendre la main"),
        ManagedSlotStatus.Missing => View(
            HomeVisualState.ManagedSlotSetup,
            "Configurons ce PC",
            "Une configuration unique en deux étapes est nécessaire avant la première prise en main sur ce PC.",
            1,
            HomePrimaryAction.ConfigureManagedSlot,
            "Configurer ce PC"),
        ManagedSlotStatus.LegacyCandidate => View(
            HomeVisualState.ManagedSlotRebind,
            "Un monde partagé existant a été trouvé",
            "Rattachez-le pour continuer à l'utiliser comme emplacement permanent de ce PC.",
            1,
            HomePrimaryAction.BindExistingManagedSlot,
            "Rattacher ce monde"),
        _ => View(
            HomeVisualState.SafetyStop,
            "Le slot du monde doit être vérifié",
            "Aucune action automatique ne sera tentée avant une vérification.",
            1,
            HomePrimaryAction.OpenDiagnostics,
            "Ouvrir l'assistance")
    };

    private static HomeViewState PresentLocal(TransferSession session, bool gameRunning) => session.Stage switch
    {
        TransferStage.AwaitingPlaceholder when session.FlowKind == TransferFlowKind.InitialSlotSetup => View(
            HomeVisualState.ManagedSlotConfigureStep1,
            "Configuration unique — étape 1 sur 2",
            "Copiez ce nom, lancez le jeu, créez un monde portant exactement ce nom, entrez-y une fois, sauvegardez puis fermez complètement.",
            2,
            HomePrimaryAction.LaunchGame,
            "Lancer The Planet Crafter",
            slotName: ManagedSlotResolver.PermanentDisplayName,
            showCopySlotName: true),
        TransferStage.AwaitingPlaceholder => View(
            HomeVisualState.Placeholder,
            "Créons l'emplacement du monde",
            $"Lancez le jeu et créez le monde « {session.PlaceholderName ?? "indiqué"} ».",
            2,
            HomePrimaryAction.LaunchGame,
            "Lancer The Planet Crafter"),
        TransferStage.Importing when session.FlowKind == TransferFlowKind.InitialSlotSetup => View(
            HomeVisualState.ManagedSlotInstalling,
            "Installation du monde partagé…",
            "GameSave Hub installe la sauvegarde dans l'emplacement que vous venez de créer.",
            2,
            indeterminate: true),
        TransferStage.ReadyToPlay when session.FlowKind == TransferFlowKind.InitialSlotSetup => View(
            HomeVisualState.ManagedSlotConfigureStep2,
            "Configuration unique — étape 2 sur 2",
            "Votre PC est configuré. Lancez le jeu pour rejoindre le monde partagé.",
            2,
            HomePrimaryAction.LaunchGame,
            "Lancer The Planet Crafter"),
        TransferStage.ReadyToPlay => View(
            HomeVisualState.ReadyToPlay,
            "Tout est prêt pour jouer",
            "La sauvegarde la plus récente est installée sur ce PC.",
            2,
            HomePrimaryAction.LaunchGame,
            "Lancer The Planet Crafter"),
        TransferStage.InGame when gameRunning => View(
            HomeVisualState.Hosting,
            "Vous hébergez la partie",
            "Jouez normalement. La sauvegarde sera sécurisée après la fermeture du jeu.",
            3),
        TransferStage.InGame => View(
            HomeVisualState.Securing,
            "Sécurisation de la partie…",
            "Le jeu est fermé ; la sauvegarde va être contrôlée et publiée.",
            3,
            indeterminate: true),
        TransferStage.CapturingResult or TransferStage.UploadPending or TransferStage.Uploading or TransferStage.Publishing => View(
            HomeVisualState.Securing,
            "Sécurisation de la partie…",
            "Ne relancez pas le jeu pendant la publication de la sauvegarde.",
            3,
            indeterminate: true),
        TransferStage.Interrupted => View(
            HomeVisualState.Interrupted,
            "La partie peut être reprise en sécurité",
            "Le dernier traitement a été interrompu avant sa fin. Les détails techniques sont conservés dans le diagnostic.",
            2,
            HomePrimaryAction.ResumeTransfer,
            "Reprendre en sécurité"),
        TransferStage.ManualReview => View(
            HomeVisualState.ManualReview,
            "Une vérification est nécessaire",
            "Aucune écriture automatique ne sera tentée avant le diagnostic.",
            2,
            HomePrimaryAction.OpenDiagnostics,
            "Ouvrir l'assistance"),
        _ when session.FlowKind == TransferFlowKind.InitialSlotSetup => View(
            HomeVisualState.ManagedSlotInstalling,
            "Installation du monde partagé…",
            "GameSave Hub prépare la configuration unique de ce PC.",
            2,
            indeterminate: true),
        _ => View(
            HomeVisualState.Preparing,
            "Préparation de votre partie…",
            "GameSave Hub récupère et prépare la dernière version du monde.",
            2,
            indeterminate: true)
    };

    private static HomeViewState PresentRemote(string? playerName, string state, bool gameRunning)
    {
        var host = string.IsNullOrWhiteSpace(playerName) ? "Un autre joueur" : playerName.Trim();
        if (!state.Equals("InGame", StringComparison.OrdinalIgnoreCase))
            return View(HomeVisualState.RemotePreparing, $"{host} prépare le monde", "Vous pourrez rejoindre dès que la préparation sera terminée.", 2, indeterminate: true);
        return gameRunning
            ? View(HomeVisualState.RemoteHosting, $"The Planet Crafter est ouvert — {host} héberge", "Rejoignez sa partie depuis le menu multijoueur du jeu.", 3)
            : View(HomeVisualState.RemoteHosting, $"{host} héberge actuellement", "Lancez le jeu puis rejoignez sa partie depuis le menu multijoueur.", 3, HomePrimaryAction.LaunchGame, "Lancer The Planet Crafter");
    }

    private static HomeViewState View(
        HomeVisualState state,
        string title,
        string instruction,
        int progressStep,
        HomePrimaryAction action = HomePrimaryAction.None,
        string? actionLabel = null,
        bool indeterminate = false,
        string? slotName = null,
        bool showCopySlotName = false) =>
        new(state, title, instruction, action, actionLabel, progressStep, indeterminate, slotName, showCopySlotName);
}
