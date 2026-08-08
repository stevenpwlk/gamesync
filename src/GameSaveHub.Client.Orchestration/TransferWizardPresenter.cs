namespace GameSaveHub.Client.Orchestration;

/// <summary>
/// Tonalité visuelle attendue pour l'étape courante.
/// </summary>
public enum WizardTone
{
    Neutral,
    Progress,
    Action,
    Success,
    Warning,
    Danger
}

/// <summary>
/// Action unique proposée à l'utilisateur. <see cref="Command"/> à <c>null</c> signifie
/// que l'action est purement locale (fermer un récapitulatif) et n'appelle pas le service.
/// </summary>
public sealed record WizardAction(string Label, string? Command);

/// <summary>
/// Description complète de ce que l'écran doit afficher pour une étape donnée.
/// </summary>
public sealed record WizardView(
    string Title,
    string Instruction,
    IReadOnlyList<string> Steps,
    string? PlaceholderName,
    string? Detail,
    WizardAction? PrimaryAction,
    bool ShowAbort,
    bool IsWaitingOnService,
    WizardTone Tone,
    int StepNumber = 0,
    int StepCount = TransferWizardPresenter.NominalStepCount);

/// <summary>
/// Traduit l'état d'une session de transfert en instructions pour un joueur.
/// </summary>
/// <remarks>
/// Cette classe est volontairement pure et sans dépendance à WPF : c'est elle qui porte
/// la règle « une seule action possible à la fois », et elle doit rester testable.
/// L'application n'est qu'un rendu de ce qu'elle décrit.
///
/// La sûreté réelle n'est pas ici : <c>PipeServerWorker.StartTransferAsync</c> rejoue le
/// préflight et <c>TransferOrchestrator</c> refuse toute commande hors séquence. Cette
/// classe évite seulement de proposer à l'utilisateur une action qui serait refusée.
/// </remarks>
public static class TransferWizardPresenter
{
    /// <summary>
    /// Nombre d'étapes visibles par le joueur. Savoir qu'il en reste deux plutôt
    /// qu'un nombre indéterminé change tout quand on suit une procédure à distance.
    /// </summary>
    public const int NominalStepCount = 6;

    public const string StartCommand = "transfer-start";
    public const string PlaceholderReadyCommand = "transfer-placeholder-ready";
    public const string PlayStartedCommand = "transfer-play-started";
    public const string PlayCompleteCommand = "transfer-play-complete";
    public const string ResumeCommand = "transfer-resume";
    public const string AbortCommand = "transfer-abort";

    private static readonly string[] NoSteps = [];

    public static WizardView Describe(
        TransferSession? session,
        bool preflightCompatible,
        bool wgsTransferEnabled)
    {
        if (session is null)
        {
            return DescribeIdle(preflightCompatible, wgsTransferEnabled);
        }

        var canAbort = TransferStageRules.CanAbortBeforeImport(session);
        var detail = FormatError(session);

        return session.Stage switch
        {
            TransferStage.Initialized or
            TransferStage.Acquiring or
            TransferStage.DownloadingArtifact or
            TransferStage.PreparingArtifact or
            TransferStage.CreatingBaseline => new WizardView(
                "Préparation du transfert",
                "GameSave Hub réserve le monde, télécharge la sauvegarde et met vos mondes existants à l'abri. Laissez l'application ouverte.",
                NoSteps,
                null,
                detail,
                null,
                canAbort,
                true,
                WizardTone.Progress,
                StepNumber: 1),

            TransferStage.AwaitingPlaceholder => new WizardView(
                "Créez le monde d'accueil dans Planet Crafter",
                "GameSave Hub n'écrase jamais un monde existant : il lui faut un monde neuf, créé par vous, qui servira de destination.",
                [
                    "Lancez The Planet Crafter.",
                    "Créez une nouvelle partie et nommez-la exactement comme le nom affiché ci-dessous.",
                    "Sauvegardez, puis quittez complètement le jeu.",
                    "Attendez que l'application Xbox affiche « Synchronisé »."
                ],
                session.PlaceholderName,
                detail,
                new WizardAction("J'ai créé le monde", PlaceholderReadyCommand),
                canAbort,
                false,
                WizardTone.Action,
                StepNumber: 2),

            TransferStage.Importing => new WizardView(
                "Import en cours",
                "Ne lancez pas le jeu et ne fermez pas l'application tant que cette étape n'est pas terminée.",
                NoSteps,
                session.PlaceholderName,
                detail,
                null,
                false,
                true,
                WizardTone.Progress,
                StepNumber: 3),

            TransferStage.ReadyToPlay => new WizardView(
                "La sauvegarde est prête",
                string.IsNullOrWhiteSpace(session.TargetDisplayName)
                    ? "Lancez The Planet Crafter et chargez le monde qui vient d'être préparé."
                    : $"Lancez The Planet Crafter et chargez le monde « {session.TargetDisplayName} ».",
                [
                    "Lancez The Planet Crafter.",
                    "Chargez le monde préparé.",
                    "Revenez ici confirmer que le jeu est lancé."
                ],
                session.PlaceholderName,
                detail,
                new WizardAction("J'ai lancé le jeu", PlayStartedCommand),
                false,
                false,
                WizardTone.Action,
                StepNumber: 4),

            TransferStage.InGame => new WizardView(
                "Partie en cours",
                "Jouez normalement. Quand vous avez terminé, sauvegardez dans le jeu puis fermez-le complètement avant de confirmer ici.",
                [
                    "Sauvegardez votre partie dans le jeu.",
                    "Quittez complètement The Planet Crafter.",
                    "Attendez que Xbox affiche « Synchronisé »."
                ],
                null,
                detail,
                new WizardAction("J'ai sauvegardé et fermé le jeu", PlayCompleteCommand),
                false,
                false,
                WizardTone.Action,
                StepNumber: 4),

            TransferStage.CapturingResult or
            TransferStage.UploadPending or
            TransferStage.Uploading or
            TransferStage.Publishing => new WizardView(
                "Envoi de votre partie",
                "GameSave Hub capture votre sauvegarde et la publie sur le NAS. Laissez l'application ouverte jusqu'à la fin.",
                NoSteps,
                null,
                detail,
                null,
                false,
                true,
                WizardTone.Progress,
                StepNumber: 5),

            TransferStage.Completed => new WizardView(
                "Transfert terminé",
                "Votre partie a été publiée. Le monde est de nouveau disponible pour les autres joueurs.",
                NoSteps,
                null,
                session.ResultVersionId is null ? detail : $"Version publiée : {session.ResultVersionId}",
                new WizardAction("Fermer", null),
                false,
                false,
                WizardTone.Success,
                StepNumber: 6),

            TransferStage.Interrupted => new WizardView(
                "Session interrompue",
                "Rien n'a été perdu et rien ne sera réécrit automatiquement. Vous pouvez reprendre là où la session s'est arrêtée.",
                NoSteps,
                session.PlaceholderName,
                detail,
                new WizardAction("Reprendre", ResumeCommand),
                canAbort,
                false,
                WizardTone.Warning),

            TransferStage.ManualReview => new WizardView(
                "Vérification manuelle nécessaire",
                "GameSave Hub a détecté une situation qu'il refuse de traiter automatiquement. Aucune écriture ne sera faite. Transmettez le détail ci-dessous avant toute nouvelle tentative.",
                NoSteps,
                session.PlaceholderName,
                detail,
                null,
                false,
                false,
                WizardTone.Danger),

            TransferStage.Aborted => new WizardView(
                "Transfert abandonné",
                "La session a été abandonnée avant toute écriture. Aucun de vos mondes n'a été modifié.",
                NoSteps,
                null,
                detail,
                new WizardAction("Fermer", null),
                false,
                false,
                WizardTone.Neutral),

            TransferStage.Failed => new WizardView(
                "Transfert en échec",
                "La session s'est arrêtée avant l'import. Vos mondes existants n'ont pas été modifiés.",
                NoSteps,
                null,
                detail,
                new WizardAction("Fermer", null),
                false,
                false,
                WizardTone.Danger),

            _ => new WizardView(
                "État inattendu",
                $"L'étape « {session.Stage} » n'est pas reconnue par cette version de l'application.",
                NoSteps,
                null,
                detail,
                null,
                false,
                false,
                WizardTone.Danger)
        };
    }

    private static WizardView DescribeIdle(bool preflightCompatible, bool wgsTransferEnabled)
    {
        if (!wgsTransferEnabled)
        {
            return new WizardView(
                "Aucun transfert en cours",
                "L'écriture des sauvegardes est désactivée sur ce PC. C'est volontaire : le transfert d'hôte reste fermé tant que la validation n'est pas terminée.",
                NoSteps,
                null,
                null,
                null,
                false,
                false,
                WizardTone.Neutral);
        }

        return preflightCompatible
            ? new WizardView(
                "Prêt à démarrer",
                "Votre pseudo a été trouvé dans cette sauvegarde. Vous pouvez prendre la main sur ce monde.",
                NoSteps,
                null,
                null,
                new WizardAction("Démarrer le transfert", StartCommand),
                false,
                false,
                WizardTone.Action)
            : new WizardView(
                "Aucun transfert en cours",
                "Sélectionnez un monde puis lancez la vérification de compatibilité.",
                NoSteps,
                null,
                null,
                null,
                false,
                false,
                WizardTone.Neutral);
    }

    private static string? FormatError(TransferSession session) =>
        string.IsNullOrWhiteSpace(session.LastErrorCode) && string.IsNullOrWhiteSpace(session.LastErrorMessage)
            ? null
            : string.IsNullOrWhiteSpace(session.LastErrorMessage)
                ? session.LastErrorCode
                : $"{session.LastErrorCode} — {session.LastErrorMessage}";
}
