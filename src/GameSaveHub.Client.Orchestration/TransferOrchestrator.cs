using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Orchestration;

public sealed class TransferOrchestrator(
    IGameSaveAdapter adapter,
    ITransferServerClient server,
    ITransferSessionStore store,
    IManagedSlotStore managedSlotStore,
    TimeProvider? timeProvider = null)
{
    private const int DefaultUploadChunkSize = 4 * 1024 * 1024;
    private static readonly TimeSpan SaveStabilityWindow = TimeSpan.FromSeconds(2);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<TransferOperationResult> StartAsync(
        Guid worldId,
        string playerName,
        TransferFlowKind flowKind = TransferFlowKind.LegacyPlaceholder,
        CancellationToken cancellationToken = default)
    {
        if (worldId == Guid.Empty) return Failure("world_required", "L'identifiant du monde est obligatoire.", null);
        if (string.IsNullOrWhiteSpace(playerName)) return Failure("player_required", "Le pseudo du joueur est obligatoire.", null);
        if (!adapter.Capabilities.CanPrepareForHost || !adapter.Capabilities.CanImportPortableArtifact)
        {
            return Failure("adapter_not_ready", "L'adaptateur n'est pas validé pour la préparation et l'import.", null);
        }

        var active = await store.ReadActiveAsync(cancellationToken);
        if (active.Count > 0)
        {
            return Failure("active_session_exists", $"Une session locale est déjà active : {active[0].LocalSessionId:D}.", active[0]);
        }

        ManagedSlotBinding? binding = null;
        if (flowKind == TransferFlowKind.ManagedSlotReuse)
        {
            binding = await managedSlotStore.ReadAsync(cancellationToken);
            if (binding is null)
            {
                return Failure("managed_slot_binding_missing", "Aucun slot géré n'est enregistré pour une réutilisation.", null);
            }
        }

        var session = TransferSession.Create(worldId, playerName.Trim(), _clock.GetUtcNow(), flowKind);
        if (binding is not null)
        {
            session = session with
            {
                TargetLogicalName = binding.LogicalName,
                ManagedSlotCurrentDisplayName = binding.CurrentDisplayName,
                ManagedSlotDesiredDisplayName = binding.DesiredDisplayName
            };
        }
        session = await PersistAsync(session, "session-created", cancellationToken);
        return await AdvancePreparationAsync(session, cancellationToken);
    }

    public async Task<TransferOperationResult> GetStatusAsync(Guid localSessionId, CancellationToken cancellationToken = default)
    {
        var session = await store.ReadAsync(localSessionId, cancellationToken);
        return session is null
            ? Failure("session_not_found", "Session locale introuvable.", null)
            : Success("status", "État de la session locale.", session);
    }

    /// <summary>
    /// Toutes les sessions locales, terminées comprises, pour le rapport de diagnostic.
    /// </summary>
    public async Task<IReadOnlyList<TransferSession>> GetAllSessionsAsync(CancellationToken cancellationToken = default) =>
        await store.ReadAllAsync(cancellationToken);

    public async Task<IReadOnlyList<TransferSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default) =>
        await store.ReadActiveAsync(cancellationToken);

    public async Task<TransferOperationResult> ConfirmPlaceholderReadyAsync(
        Guid localSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(localSessionId, cancellationToken);
        if (session is null) return Failure("session_not_found", "Session locale introuvable.", null);
        if (session.Stage != TransferStage.AwaitingPlaceholder)
        {
            return Failure("invalid_stage", $"Placeholder impossible à valider dans l'état {session.Stage}.", session);
        }
        if (session.BaselineDirectory is null || session.PreparedArtifactPath is null || session.PlaceholderName is null)
        {
            return await ManualReviewAsync(session, "checkpoint_incomplete", "Le checkpoint d'import est incomplet.", cancellationToken);
        }

        var probe = await adapter.ProbeImportTargetAsync(session.BaselineDirectory, session.PlaceholderName, cancellationToken);
        if (!probe.Success || probe.TargetLogicalName is null || probe.PlaceholderPayloadSha256 is null)
        {
            return await RecordNonTerminalErrorAsync(session, "placeholder_invalid", string.Join("; ", probe.Errors), cancellationToken);
        }

        session = await PersistAsync(session with
        {
            Stage = TransferStage.Importing,
            TargetLogicalName = probe.TargetLogicalName,
            TargetDisplayName = probe.TargetDisplayName,
            PlaceholderPayloadSha256 = probe.PlaceholderPayloadSha256,
            ResumeStage = null,
            LastErrorCode = null,
            LastErrorMessage = null
        }, "import-checkpoint-created", cancellationToken);

        return await PerformImportAsync(session, cancellationToken);
    }

    public async Task<TransferOperationResult> MarkPlayStartedAsync(
        Guid localSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(localSessionId, cancellationToken);
        if (session is null) return Failure("session_not_found", "Session locale introuvable.", null);
        if (session.Stage != TransferStage.ReadyToPlay)
        {
            return Failure("invalid_stage", $"Le démarrage du jeu n'est pas attendu dans l'état {session.Stage}.", session);
        }
        var process = await adapter.DetectGameProcessAsync(cancellationToken);
        if (!process.IsRunning)
        {
            return await RecordNonTerminalErrorAsync(session, "game_not_running", "Planet Crafter n'est pas détecté comme lancé.", cancellationToken);
        }

        session = await PersistAsync(session with { Stage = TransferStage.InGame, LastErrorCode = null, LastErrorMessage = null }, "game-started", cancellationToken);
        await TryHeartbeatAsync(session, "in-game", cancellationToken);
        return Success("in_game", "Le jeu est détecté. La session reste verrouillée jusqu'à publication.", session);
    }

    public async Task<TransferOperationResult> CompletePlayAsync(
        Guid localSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(localSessionId, cancellationToken);
        if (session is null) return Failure("session_not_found", "Session locale introuvable.", null);
        if (session.Stage is not TransferStage.ReadyToPlay and not TransferStage.InGame and not TransferStage.CapturingResult)
        {
            return Failure("invalid_stage", $"La capture finale n'est pas possible dans l'état {session.Stage}.", session);
        }
        return await CaptureAndPublishAsync(session, cancellationToken);
    }

    public async Task<TransferOperationResult> ResumeAsync(
        Guid localSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(localSessionId, cancellationToken);
        if (session is null) return Failure("session_not_found", "Session locale introuvable.", null);

        if (session.Stage == TransferStage.Interrupted && session.ResumeStage is TransferStage resumeStage)
        {
            session = await PersistAsync(session with
            {
                Stage = resumeStage,
                ResumeStage = null,
                LastErrorCode = null,
                LastErrorMessage = null
            }, "explicit-resume", cancellationToken);
        }

        return session.Stage switch
        {
            TransferStage.Initialized or TransferStage.Acquiring or TransferStage.DownloadingArtifact or
            TransferStage.PreparingArtifact or TransferStage.CreatingBaseline => await AdvancePreparationAsync(session, cancellationToken),
            TransferStage.Importing => await RecoverImportAsync(session, allowRetry: true, cancellationToken),
            TransferStage.CapturingResult => await CaptureAndPublishAsync(session, cancellationToken),
            TransferStage.UploadPending or TransferStage.Uploading or TransferStage.Publishing => await PublishAsync(session, cancellationToken),
            TransferStage.AwaitingPlaceholder => Success("awaiting_placeholder", "Créez le placeholder demandé dans Planet Crafter puis confirmez-le.", session),
            TransferStage.ReadyToPlay => Success("ready_to_play", "L'import est prêt. Lancez Planet Crafter manuellement.", session),
            TransferStage.InGame => Success("in_game", "La session était en jeu. Fermez Planet Crafter après sauvegarde puis confirmez la fin.", session),
            TransferStage.ManualReview => Failure("manual_review", "La session nécessite une analyse manuelle avant toute nouvelle écriture.", session),
            _ => Failure("resume_not_available", $"Aucune reprise n'est disponible dans l'état {session.Stage}.", session)
        };
    }

    public async Task<IReadOnlyList<TransferOperationResult>> RecoverAllAsync(CancellationToken cancellationToken = default)
    {
        var active = await store.ReadActiveAsync(cancellationToken);
        var results = new List<TransferOperationResult>(active.Count);
        if (active.Count > 1)
        {
            foreach (var session in active.OrderBy(item => item.CreatedAtUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await ManualReviewAsync(
                    session,
                    "multiple_active_sessions",
                    "Plusieurs sessions locales actives ont été détectées. Aucune reprise automatique n'est autorisée.",
                    cancellationToken));
            }
            return results;
        }
        foreach (var session in active)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RecoverOneAsync(session, cancellationToken));
        }
        return results;
    }

    public async Task<TransferOperationResult> AbortAsync(Guid localSessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(localSessionId, cancellationToken);
        if (session is null) return Failure("session_not_found", "Session locale introuvable.", null);
        if (!TransferStageRules.CanAbortBeforeImport(session))
        {
            return Failure("abort_forbidden", "Après le début de l'import serveur, la session ne peut plus être abandonnée localement.", session);
        }

        try
        {
            if (session.ServerSessionId is Guid serverSessionId)
            {
                await server.AbortSessionAsync(serverSessionId, cancellationToken);
            }
            session = await PersistAsync(session with { Stage = TransferStage.Aborted, ResumeStage = null }, "session-aborted", cancellationToken);
            return Success("aborted", "Session abandonnée avant import.", session);
        }
        catch (TransferServerException exception)
        {
            return await RecordNonTerminalErrorAsync(session, exception.Code, exception.Message, cancellationToken);
        }
    }

    private async Task<TransferOperationResult> AdvancePreparationAsync(TransferSession session, CancellationToken cancellationToken)
    {
        try
        {
            if (session.Stage is TransferStage.Initialized or TransferStage.Acquiring)
            {
                session = await PersistAsync(session with { Stage = TransferStage.Acquiring }, "acquire-starting", cancellationToken);
                if (session.ServerSessionId is null)
                {
                    var status = await server.GetWorldStatusAsync(session.WorldId, cancellationToken);
                    var acquired = await server.AcquireWorldAsync(
                        session.WorldId,
                        status.CurrentVersionId,
                        session.PlayerName,
                        session.AcquireIdempotencyKey,
                        cancellationToken);
                    session = await PersistAsync(session with
                    {
                        ServerSessionId = acquired.SessionId,
                        BaseVersionId = acquired.CurrentVersionId,
                        Stage = TransferStage.DownloadingArtifact
                    }, "server-acquired", cancellationToken);
                }
                else
                {
                    session = await PersistAsync(session with { Stage = TransferStage.DownloadingArtifact }, "acquire-already-checkpointed", cancellationToken);
                }
            }

            if (session.Stage == TransferStage.DownloadingArtifact)
            {
                if (session.ServerSessionId is not Guid serverSessionId)
                {
                    return await ManualReviewAsync(session, "server_session_missing", "Session serveur absente après acquisition.", cancellationToken);
                }
                if (session.DownloadedArtifactPath is null || !File.Exists(session.DownloadedArtifactPath))
                {
                    var inbound = Path.Combine(store.GetSessionDirectory(session.LocalSessionId), "inbound");
                    Directory.CreateDirectory(inbound);
                    var destination = Path.Combine(inbound, "source.gshsave");
                    var download = await server.DownloadSessionArtifactAsync(serverSessionId, destination, cancellationToken);
                    if (!download.HasArtifact || download.Path is null || download.Sha256 is null)
                    {
                        return await FailBeforeImportAsync(session, "artifact_missing", "Le monde serveur n'a pas encore d'artefact exploitable.", cancellationToken);
                    }
                    session = await PersistAsync(session with
                    {
                        DownloadedArtifactPath = download.Path,
                        DownloadedArtifactSha256 = download.Sha256,
                        Stage = TransferStage.PreparingArtifact
                    }, "artifact-downloaded", cancellationToken);
                }
                else
                {
                    session = await PersistAsync(session with { Stage = TransferStage.PreparingArtifact }, "artifact-download-already-checkpointed", cancellationToken);
                }
            }

            if (session.Stage == TransferStage.PreparingArtifact)
            {
                if (session.DownloadedArtifactPath is null || !File.Exists(session.DownloadedArtifactPath))
                {
                    return await FailBeforeImportAsync(session, "source_artifact_missing", "L'artefact source local a disparu.", cancellationToken);
                }
                var source = ArtifactFromPath(session.DownloadedArtifactPath);
                var validation = await adapter.ValidateArtifactAsync(source, cancellationToken);
                if (!validation.IsValid)
                {
                    return await FailBeforeImportAsync(session, "artifact_invalid", string.Join("; ", validation.Errors), cancellationToken);
                }
                if (session.PreparedArtifactPath is null || !File.Exists(session.PreparedArtifactPath))
                {
                    var preparedRoot = Path.Combine(store.GetSessionDirectory(session.LocalSessionId), "prepared");
                    var preparation = await adapter.PrepareForHostAsync(
                        source,
                        session.PlayerName,
                        ManagedSlotResolver.PermanentDisplayName,
                        preparedRoot,
                        cancellationToken);
                    if (!preparation.Success || preparation.PreparedArtifact is null)
                    {
                        var code = preparation.Outcome switch
                        {
                            HostPreparationOutcome.PlayerNotFound => "player_not_found",
                            HostPreparationOutcome.PlayerAmbiguous => "player_ambiguous",
                            HostPreparationOutcome.InvalidDisplayName => "invalid_target_display_name",
                            HostPreparationOutcome.InvalidPlayerTopology => "player_topology_invalid",
                            HostPreparationOutcome.InvalidArtifact => "artifact_invalid",
                            _ => "host_preparation_failed"
                        };
                        return await FailBeforeImportAsync(session, code, string.Join("; ", preparation.Errors), cancellationToken);
                    }
                    session = await PersistAsync(session with
                    {
                        PreparedArtifactPath = preparation.PreparedArtifact.Path,
                        PreparedArtifactSha256 = preparation.PreparedArtifact.Sha256,
                        PreparedPayloadSha256 = preparation.PreparedArtifact.Manifest?.PayloadSha256,
                        Stage = TransferStage.CreatingBaseline
                    }, "artifact-prepared", cancellationToken);
                }
                else
                {
                    session = await PersistAsync(session with { Stage = TransferStage.CreatingBaseline }, "artifact-preparation-already-checkpointed", cancellationToken);
                }
            }

            if (session.Stage == TransferStage.CreatingBaseline)
            {
                if (session.PreparedArtifactPath is null || !File.Exists(session.PreparedArtifactPath))
                {
                    return await FailBeforeImportAsync(session, "prepared_artifact_missing", "L'artefact préparé a disparu.", cancellationToken);
                }

                if (session.FlowKind == TransferFlowKind.ManagedSlotReuse)
                {
                    if (session.TargetLogicalName is null || session.ManagedSlotCurrentDisplayName is null || session.ManagedSlotDesiredDisplayName is null)
                    {
                        return await ManualReviewAsync(session, "managed_slot_binding_incomplete", "Le slot géré référencé est incomplet.", cancellationToken);
                    }
                    if (session.BaselineDirectory is null || !Directory.Exists(session.BaselineDirectory))
                    {
                        var baselineRoot = Path.Combine(store.GetSessionDirectory(session.LocalSessionId), "safety", "managed-slot-baselines");
                        var slot = new ManagedSlotReference(session.TargetLogicalName, session.ManagedSlotCurrentDisplayName, session.ManagedSlotDesiredDisplayName);
                        var managedBaseline = await adapter.CreateManagedSlotBaselineAsync(slot, baselineRoot, cancellationToken);
                        if (!managedBaseline.Success || managedBaseline.BaselineDirectory is null)
                        {
                            return await FailBeforeImportAsync(session, "managed_slot_baseline_failed", string.Join("; ", managedBaseline.Errors), cancellationToken);
                        }
                        session = await PersistAsync(session with
                        {
                            BaselineDirectory = managedBaseline.BaselineDirectory,
                            Stage = TransferStage.Importing
                        }, "managed-slot-baseline-created", cancellationToken);
                    }
                    else
                    {
                        session = await PersistAsync(session with { Stage = TransferStage.Importing }, "managed-slot-baseline-already-checkpointed", cancellationToken);
                    }
                    return await PerformImportAsync(session, cancellationToken);
                }

                if (session.BaselineDirectory is null || !Directory.Exists(session.BaselineDirectory))
                {
                    var baselineRoot = Path.Combine(store.GetSessionDirectory(session.LocalSessionId), "safety", "import-baselines");
                    var baseline = await adapter.CreateImportBaselineAsync(baselineRoot, cancellationToken);
                    if (!baseline.Success || baseline.BaselineDirectory is null)
                    {
                        return await FailBeforeImportAsync(session, "baseline_failed", string.Join("; ", baseline.Errors), cancellationToken);
                    }
                    session = await PersistAsync(session with
                    {
                        BaselineDirectory = baseline.BaselineDirectory,
                        PlaceholderName = session.FlowKind == TransferFlowKind.InitialSlotSetup ? ManagedSlotResolver.PermanentDisplayName : GeneratePlaceholderName(),
                        Stage = TransferStage.AwaitingPlaceholder
                    }, "baseline-created", cancellationToken);
                }
                else
                {
                    session = await PersistAsync(session with
                    {
                        PlaceholderName = session.PlaceholderName ??
                            (session.FlowKind == TransferFlowKind.InitialSlotSetup ? ManagedSlotResolver.PermanentDisplayName : GeneratePlaceholderName()),
                        Stage = TransferStage.AwaitingPlaceholder
                    }, "baseline-already-checkpointed", cancellationToken);
                }
            }

            await TryHeartbeatAsync(session, "awaiting-placeholder", cancellationToken);
            return Success("awaiting_placeholder", $"Créez un nouveau monde nommé exactement {session.PlaceholderName}, sauvegardez, fermez le jeu et attendez la synchronisation Xbox.", session);
        }
        catch (TransferServerException exception)
        {
            return await FailBeforeImportAsync(session, exception.Code, exception.Message, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return await FailBeforeImportAsync(session, "preparation_failed", exception.Message, cancellationToken);
        }
    }

    private async Task<TransferOperationResult> PerformImportAsync(TransferSession session, CancellationToken cancellationToken)
    {
        if (session.FlowKind == TransferFlowKind.ManagedSlotReuse)
        {
            return await PerformManagedSlotReplacementAsync(session, cancellationToken);
        }

        if (session.ServerSessionId is not Guid serverSessionId || session.PreparedArtifactPath is not string preparedArtifactPath ||
            session.BaselineDirectory is not string baselineDirectory || session.PlaceholderName is not string placeholderName)
        {
            return await ManualReviewAsync(session, "import_checkpoint_incomplete", "Le checkpoint d'import est incomplet.", cancellationToken);
        }

        try
        {
            if (!session.ServerImportStarted)
            {
                await server.MarkImportStartingAsync(serverSessionId, cancellationToken);
                session = await PersistAsync(session with { ServerImportStarted = true }, "server-import-started", cancellationToken);
            }

            var artifact = ArtifactFromPath(preparedArtifactPath);
            var backupRoot = Path.Combine(store.GetSessionDirectory(session.LocalSessionId), "safety", "pre-import");
            var import = await adapter.ImportPortableArtifactAsync(
                artifact,
                baselineDirectory,
                session.PlayerName,
                placeholderName,
                backupRoot,
                cancellationToken);
            if (!import.Success)
            {
                var message = string.Join("; ", import.Errors);
                await TryReportFailureAsync(session, "local_import_failed", message, cancellationToken);
                session = await PersistAsync(session with
                {
                    Stage = TransferStage.Interrupted,
                    ResumeStage = TransferStage.Importing,
                    PreImportSnapshotDirectory = import.PreImportSnapshotDirectory,
                    LastErrorCode = "local_import_failed",
                    LastErrorMessage = message
                }, "import-interrupted", cancellationToken);
                return Failure("local_import_failed", message, session);
            }

            session = await PersistAsync(session with
            {
                Stage = TransferStage.ReadyToPlay,
                ResumeStage = null,
                TargetLogicalName = import.TargetLogicalName ?? session.TargetLogicalName,
                TargetDisplayName = import.TargetDisplayName,
                PreImportSnapshotDirectory = import.PreImportSnapshotDirectory,
                ImportedPayloadSha256 = import.ImportedPayloadSha256,
                LastErrorCode = null,
                LastErrorMessage = null
            }, "import-completed", cancellationToken);
            session = await CommitManagedSlotBindingIfNeededAsync(session, cancellationToken);
            await TryHeartbeatAsync(session, "ready-to-play", cancellationToken);
            return Success("ready_to_play", $"Import validé dans {session.TargetLogicalName}. Lancez Planet Crafter manuellement et ouvrez uniquement le monde importé.", session);
        }
        catch (TransferServerException exception)
        {
            session = await PersistAsync(session with
            {
                Stage = TransferStage.Interrupted,
                ResumeStage = TransferStage.Importing,
                LastErrorCode = exception.Code,
                LastErrorMessage = exception.Message
            }, "import-server-interrupted", cancellationToken);
            return Failure(exception.Code, exception.Message, session);
        }
    }

    private async Task<TransferOperationResult> RecoverImportAsync(TransferSession session, bool allowRetry, CancellationToken cancellationToken)
    {
        if (session.FlowKind == TransferFlowKind.ManagedSlotReuse)
        {
            return await RecoverManagedSlotReuseAsync(session, allowRetry, cancellationToken);
        }

        if (session.PreparedArtifactPath is null || session.BaselineDirectory is null || session.TargetLogicalName is null ||
            session.PlaceholderPayloadSha256 is null)
        {
            return await ManualReviewAsync(session, "reconcile_checkpoint_incomplete", "Impossible de réconcilier l'import : checkpoint incomplet.", cancellationToken);
        }
        if (!File.Exists(session.PreparedArtifactPath))
        {
            return await ManualReviewAsync(session, "prepared_artifact_missing", "L'artefact préparé n'existe plus.", cancellationToken);
        }

        var reconciliation = await adapter.ReconcilePortableImportAsync(
            ArtifactFromPath(session.PreparedArtifactPath),
            session.BaselineDirectory,
            session.PlayerName,
            session.TargetLogicalName,
            session.PlaceholderPayloadSha256,
            cancellationToken);

        if (reconciliation.State == ImportReconciliationState.ImportedArtifactPresent)
        {
            session = await PersistAsync(session with
            {
                Stage = TransferStage.ReadyToPlay,
                ResumeStage = null,
                ImportedPayloadSha256 = reconciliation.ExpectedImportedPayloadSha256,
                LastErrorCode = null,
                LastErrorMessage = null
            }, "import-reconciled-as-complete", cancellationToken);
            session = await CommitManagedSlotBindingIfNeededAsync(session, cancellationToken);
            await TryHeartbeatAsync(session, "ready-to-play-recovered", cancellationToken);
            return Success("ready_to_play", "L'import avait déjà été effectué avant l'interruption. Aucune nouvelle écriture WGS n'a été faite.", session);
        }

        if (reconciliation.State == ImportReconciliationState.PlaceholderIntact)
        {
            if (allowRetry)
            {
                return await PerformImportAsync(session with { Stage = TransferStage.Importing }, cancellationToken);
            }
            session = await PersistAsync(session with
            {
                Stage = TransferStage.Interrupted,
                ResumeStage = TransferStage.Importing,
                LastErrorCode = "import_not_written",
                LastErrorMessage = "Le placeholder est intact. Une reprise explicite peut retenter l'import."
            }, "import-reconciled-placeholder-intact", cancellationToken);
            return Failure("import_not_written", "Le placeholder est intact. Utilisez Reprendre pour retenter l'import explicitement.", session);
        }

        return await ManualReviewAsync(
            session,
            "import_reconciliation_failed",
            string.Join("; ", reconciliation.Errors.DefaultIfEmpty($"État de réconciliation : {reconciliation.State}.")),
            cancellationToken);
    }

    private async Task<TransferOperationResult> PerformManagedSlotReplacementAsync(TransferSession session, CancellationToken cancellationToken)
    {
        if (session.ServerSessionId is not Guid serverSessionId || session.PreparedArtifactPath is not string preparedArtifactPath ||
            session.BaselineDirectory is not string baselineDirectory || session.TargetLogicalName is not string targetLogicalName ||
            session.ManagedSlotCurrentDisplayName is not string currentDisplayName || session.ManagedSlotDesiredDisplayName is not string desiredDisplayName)
        {
            return await ManualReviewAsync(session, "managed_slot_checkpoint_incomplete", "Le checkpoint de réutilisation du slot géré est incomplet.", cancellationToken);
        }

        try
        {
            if (!session.ServerImportStarted)
            {
                await server.MarkImportStartingAsync(serverSessionId, cancellationToken);
                session = await PersistAsync(session with { ServerImportStarted = true }, "server-import-started", cancellationToken);
            }

            var artifact = ArtifactFromPath(preparedArtifactPath);
            var backupRoot = Path.Combine(store.GetSessionDirectory(session.LocalSessionId), "safety", "pre-import");
            var slot = new ManagedSlotReference(targetLogicalName, currentDisplayName, desiredDisplayName);
            var replace = await adapter.ReplaceManagedSlotAsync(artifact, baselineDirectory, slot, session.PlayerName, backupRoot, cancellationToken);
            if (!replace.Success)
            {
                var message = string.Join("; ", replace.Errors);
                await TryReportFailureAsync(session, "managed_slot_replacement_failed", message, cancellationToken);
                session = await PersistAsync(session with
                {
                    Stage = TransferStage.Interrupted,
                    ResumeStage = TransferStage.Importing,
                    PreImportSnapshotDirectory = replace.PreImportSnapshotDirectory,
                    LastErrorCode = "managed_slot_replacement_failed",
                    LastErrorMessage = message
                }, "managed-slot-replacement-interrupted", cancellationToken);
                return Failure("managed_slot_replacement_failed", message, session);
            }

            session = await PersistAsync(session with
            {
                Stage = TransferStage.ReadyToPlay,
                ResumeStage = null,
                TargetDisplayName = replace.TargetDisplayName,
                PreImportSnapshotDirectory = replace.PreImportSnapshotDirectory,
                ImportedPayloadSha256 = replace.ImportedPayloadSha256,
                LastErrorCode = null,
                LastErrorMessage = null
            }, "managed-slot-replaced", cancellationToken);
            await SyncManagedSlotBindingDisplayNameAsync(session, cancellationToken);
            await TryHeartbeatAsync(session, "ready-to-play", cancellationToken);
            return Success("ready_to_play", $"Le slot {ManagedSlotResolver.PermanentDisplayName} a été mis à jour. Lancez Planet Crafter manuellement et ouvrez uniquement ce monde.", session);
        }
        catch (TransferServerException exception)
        {
            session = await PersistAsync(session with
            {
                Stage = TransferStage.Interrupted,
                ResumeStage = TransferStage.Importing,
                LastErrorCode = exception.Code,
                LastErrorMessage = exception.Message
            }, "managed-slot-server-interrupted", cancellationToken);
            return Failure(exception.Code, exception.Message, session);
        }
    }

    private async Task<TransferOperationResult> RecoverManagedSlotReuseAsync(TransferSession session, bool allowRetry, CancellationToken cancellationToken)
    {
        if (session.PreparedArtifactPath is null || session.BaselineDirectory is null || session.TargetLogicalName is null ||
            session.ManagedSlotCurrentDisplayName is null || session.ManagedSlotDesiredDisplayName is null)
        {
            return await ManualReviewAsync(session, "reconcile_checkpoint_incomplete", "Impossible de réconcilier la réutilisation du slot géré : checkpoint incomplet.", cancellationToken);
        }
        if (!File.Exists(session.PreparedArtifactPath))
        {
            return await ManualReviewAsync(session, "prepared_artifact_missing", "L'artefact préparé n'existe plus.", cancellationToken);
        }

        var slot = new ManagedSlotReference(session.TargetLogicalName, session.ManagedSlotCurrentDisplayName, session.ManagedSlotDesiredDisplayName);
        var reconciliation = await adapter.ReconcileManagedSlotReplacementAsync(
            ArtifactFromPath(session.PreparedArtifactPath),
            session.BaselineDirectory,
            slot,
            session.PlayerName,
            cancellationToken);

        if (reconciliation.State == ManagedSlotReconciliationState.ImportedPayloadPresent)
        {
            session = await PersistAsync(session with
            {
                Stage = TransferStage.ReadyToPlay,
                ResumeStage = null,
                ImportedPayloadSha256 = reconciliation.ExpectedImportedPayloadSha256,
                LastErrorCode = null,
                LastErrorMessage = null
            }, "managed-slot-reconciled-as-complete", cancellationToken);
            await SyncManagedSlotBindingDisplayNameAsync(session, cancellationToken);
            await TryHeartbeatAsync(session, "ready-to-play-recovered", cancellationToken);
            return Success("ready_to_play", "La réutilisation du slot avait déjà été effectuée avant l'interruption. Aucune nouvelle écriture WGS n'a été faite.", session);
        }

        if (reconciliation.State == ManagedSlotReconciliationState.PreviousPayloadPresent)
        {
            if (allowRetry)
            {
                return await PerformManagedSlotReplacementAsync(session with { Stage = TransferStage.Importing }, cancellationToken);
            }
            session = await PersistAsync(session with
            {
                Stage = TransferStage.Interrupted,
                ResumeStage = TransferStage.Importing,
                LastErrorCode = "managed_slot_not_written",
                LastErrorMessage = "Le contenu précédent du slot est intact. Une reprise explicite peut retenter le remplacement."
            }, "managed-slot-reconciled-previous-intact", cancellationToken);
            return Failure("managed_slot_not_written", "Le contenu précédent est intact. Utilisez Reprendre pour retenter le remplacement explicitement.", session);
        }

        return await ManualReviewAsync(
            session,
            "managed_slot_reconciliation_failed",
            string.Join("; ", reconciliation.Errors.DefaultIfEmpty($"État de réconciliation : {reconciliation.State}.")),
            cancellationToken);
    }

    /// <summary>
    /// Après un remplacement réussi (immédiat ou reconnu par reprise), le nom affiché WGS
    /// vaut désormais <see cref="TransferSession.ManagedSlotDesiredDisplayName"/>. Sans mettre
    /// à jour le binding persistant en conséquence, la prochaine réutilisation le comparerait
    /// à l'ancien nom courant et échouerait systématiquement dès la deuxième prise en main.
    /// </summary>
    private async Task SyncManagedSlotBindingDisplayNameAsync(TransferSession session, CancellationToken cancellationToken)
    {
        if (session.ManagedSlotDesiredDisplayName is not string desiredDisplayName) return;
        var binding = await managedSlotStore.ReadAsync(cancellationToken);
        if (binding is null || binding.CurrentDisplayName.Equals(desiredDisplayName, StringComparison.Ordinal)) return;
        await managedSlotStore.WriteAsync(
            binding with { CurrentDisplayName = desiredDisplayName, LastValidatedAtUtc = _clock.GetUtcNow() },
            cancellationToken);
    }

    private async Task<TransferSession> CommitManagedSlotBindingIfNeededAsync(TransferSession session, CancellationToken cancellationToken)
    {
        if (session.FlowKind != TransferFlowKind.InitialSlotSetup || session.ManagedSlotBindingCommitted || session.TargetLogicalName is null)
        {
            return session;
        }

        var inspection = await adapter.InspectLocalStorageAsync(cancellationToken);
        var binding = ManagedSlotBinding.Create(
            adapter.Id,
            inspection.PackageFamilyName,
            session.PlayerName,
            session.TargetLogicalName,
            session.TargetDisplayName ?? ManagedSlotResolver.PermanentDisplayName,
            ManagedSlotResolver.PermanentDisplayName,
            _clock.GetUtcNow());
        await managedSlotStore.WriteAsync(binding, cancellationToken);

        return await PersistAsync(session with { ManagedSlotBindingCommitted = true }, "managed-slot-binding-committed", cancellationToken);
    }

    private async Task<TransferOperationResult> CaptureAndPublishAsync(TransferSession session, CancellationToken cancellationToken)
    {
        try
        {
            var process = await adapter.DetectGameProcessAsync(cancellationToken);
            if (process.IsRunning)
            {
                return await RecordNonTerminalErrorAsync(session, "game_still_running", "Fermez complètement Planet Crafter avant la capture finale.", cancellationToken);
            }
            session = await PersistAsync(session with { Stage = TransferStage.CapturingResult }, "capture-starting", cancellationToken);
            var stability = await adapter.WaitForSaveStabilityAsync(SaveStabilityWindow, cancellationToken);
            if (!stability.IsStable)
            {
                return await InterruptAsync(session, TransferStage.CapturingResult, "save_not_stable", "Les fichiers WGS ne sont pas encore stables : " + string.Join(", ", stability.ChangedFiles), cancellationToken);
            }
            if (session.TargetLogicalName is null)
            {
                return await ManualReviewAsync(session, "target_missing", "Le nom logique du slot importé est absent du checkpoint.", cancellationToken);
            }

            var inspection = await adapter.InspectLocalStorageAsync(cancellationToken);
            if (!inspection.Stable || inspection.GameRunning)
            {
                return await InterruptAsync(session, TransferStage.CapturingResult, "save_not_stable", "Le stockage WGS n'est pas stable.", cancellationToken);
            }
            var target = inspection.Worlds.SingleOrDefault(world => world.LogicalName.Equals(session.TargetLogicalName, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return await ManualReviewAsync(session, "target_missing", "Le monde importé n'existe plus.", cancellationToken);
            }
            var localPlayers = target.Players.Where(player => player.Id == 0 && player.IsHost).ToArray();
            if (localPlayers.Length != 1)
            {
                return await ManualReviewAsync(session, "local_player_topology_invalid", "Le monde final ne contient pas exactement un joueur ID 0 hôte.", cancellationToken);
            }

            var outboundRoot = Path.Combine(store.GetSessionDirectory(session.LocalSessionId), "outbound");
            // Par nom logique, jamais par nom affiche : apres un second import de la
            // meme sauvegarde, deux mondes portent le meme nom affiche et la capture
            // echouait sur « Sequence contains more than one matching element ».
            var artifact = await adapter.ExportPortableArtifactByLogicalNameAsync(target.LogicalName, outboundRoot, cancellationToken);
            session = await PersistAsync(session with
            {
                TargetDisplayName = target.DisplayName,
                OutboundArtifactPath = artifact.Path,
                OutboundArtifactSha256 = artifact.Sha256,
                OutboundArtifactLength = artifact.Length,
                Stage = TransferStage.UploadPending,
                ResumeStage = null,
                LastErrorCode = null,
                LastErrorMessage = null
            }, "outbound-artifact-created", cancellationToken);
            return await PublishAsync(session, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return await InterruptAsync(session, TransferStage.CapturingResult, "capture_failed", exception.Message, cancellationToken);
        }
    }

    private async Task<TransferOperationResult> PublishAsync(TransferSession session, CancellationToken cancellationToken)
    {
        if (session.ServerSessionId is not Guid serverSessionId || session.OutboundArtifactPath is not string outboundArtifactPath ||
            session.OutboundArtifactSha256 is not string outboundArtifactSha256 || session.OutboundArtifactLength is not long outboundArtifactLength)
        {
            return await ManualReviewAsync(session, "upload_checkpoint_incomplete", "Le checkpoint d'upload est incomplet.", cancellationToken);
        }
        if (!File.Exists(outboundArtifactPath))
        {
            return await ManualReviewAsync(session, "outbound_artifact_missing", "L'artefact final à publier a disparu.", cancellationToken);
        }

        try
        {
            // Publishing is a durable checkpoint created only after every chunk has been confirmed.
            // If the server commit succeeded but its response was lost, the server session may already
            // be completed/released; recreating the upload would then fail. Replaying the known commit
            // is safe and idempotent on the server and must therefore happen before CreateUpload.
            if (session.Stage == TransferStage.Publishing)
            {
                if (session.UploadId is not Guid publishingUploadId)
                {
                    return await ManualReviewAsync(session, "publishing_upload_id_missing", "Le checkpoint Publishing ne contient pas l'identifiant d'upload.", cancellationToken);
                }
                return await CommitAndCompleteAsync(
                    session,
                    publishingUploadId,
                    outboundArtifactSha256,
                    outboundArtifactLength,
                    cancellationToken);
            }

            session = await PersistAsync(session with { Stage = TransferStage.UploadPending }, "upload-manifest-starting", cancellationToken);
            var created = await server.CreateUploadAsync(
                serverSessionId,
                new CreateUploadRequest(outboundArtifactLength, outboundArtifactSha256, session.BaseVersionId, DefaultUploadChunkSize),
                cancellationToken);
            var uploadId = created.UploadId;
            var chunkSize = created.ChunkSize;
            var confirmed = created.ReceivedChunks.ToHashSet();
            session = await PersistAsync(session with
            {
                UploadId = uploadId,
                UploadChunkSize = chunkSize,
                ConfirmedChunks = confirmed.Order().ToArray(),
                Stage = TransferStage.Uploading
            }, session.UploadId is null ? "upload-created" : "upload-resynchronized", cancellationToken);

            await using var stream = new FileStream(outboundArtifactPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var count = checked((int)((stream.Length + chunkSize - 1L) / chunkSize));
            var buffer = new byte[chunkSize];
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (confirmed.Contains(index)) continue;
                var remaining = stream.Length - (long)index * chunkSize;
                var expected = checked((int)Math.Min(chunkSize, remaining));
                stream.Position = (long)index * chunkSize;
                var totalRead = 0;
                while (totalRead < expected)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(totalRead, expected - totalRead), cancellationToken);
                    if (read == 0) throw new EndOfStreamException("Fin de fichier inattendue pendant l'upload.");
                    totalRead += read;
                }
                await server.PutUploadChunkAsync(uploadId, index, buffer.AsMemory(0, expected), cancellationToken);
                confirmed.Add(index);
                session = await PersistAsync(session with { ConfirmedChunks = confirmed.Order().ToArray() }, $"upload-chunk-{index}-confirmed", cancellationToken);
            }

            session = await PersistAsync(session with { Stage = TransferStage.Publishing }, "upload-complete-commit-starting", cancellationToken);
            return await CommitAndCompleteAsync(
                session,
                uploadId,
                outboundArtifactSha256,
                outboundArtifactLength,
                cancellationToken);
        }
        catch (TransferServerException exception)
        {
            var resume = session.Stage == TransferStage.Publishing ? TransferStage.Publishing : TransferStage.Uploading;
            return await InterruptAsync(session, resume, exception.Code, exception.Message, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            var resume = session.Stage == TransferStage.Publishing ? TransferStage.Publishing : TransferStage.Uploading;
            return await InterruptAsync(session, resume, "upload_failed", exception.Message, cancellationToken);
        }
    }

    private async Task<TransferOperationResult> CommitAndCompleteAsync(
        TransferSession session,
        Guid uploadId,
        string outboundArtifactSha256,
        long outboundArtifactLength,
        CancellationToken cancellationToken)
    {
        var committed = await server.CommitUploadAsync(uploadId, cancellationToken);
        if (!committed.Sha256.Equals(outboundArtifactSha256, StringComparison.OrdinalIgnoreCase) || committed.Length != outboundArtifactLength)
        {
            return await ManualReviewAsync(session, "commit_mismatch", "Le serveur a publié un artefact dont le hash ou la longueur diffère du fichier local.", cancellationToken);
        }

        session = await PersistAsync(session with
        {
            Stage = TransferStage.Completed,
            ResumeStage = null,
            ResultVersionId = committed.VersionId,
            LastErrorCode = null,
            LastErrorMessage = null
        }, "session-completed", cancellationToken);
        return Success("completed", "Sauvegarde publiée et verrou serveur libéré.", session);
    }

    private async Task<TransferOperationResult> RecoverOneAsync(TransferSession session, CancellationToken cancellationToken)
    {
        return session.Stage switch
        {
            TransferStage.Initialized or TransferStage.Acquiring or TransferStage.DownloadingArtifact or
            TransferStage.PreparingArtifact or TransferStage.CreatingBaseline => await AdvancePreparationAsync(session, cancellationToken),
            TransferStage.Importing => await RecoverImportAsync(session, allowRetry: false, cancellationToken),
            TransferStage.CapturingResult => await CaptureAndPublishAsync(session, cancellationToken),
            TransferStage.UploadPending or TransferStage.Uploading or TransferStage.Publishing => await PublishAsync(session, cancellationToken),
            TransferStage.Interrupted when session.ResumeStage is TransferStage.UploadPending or TransferStage.Uploading or TransferStage.Publishing or TransferStage.CapturingResult =>
                await ResumeAsync(session.LocalSessionId, cancellationToken),
            TransferStage.AwaitingPlaceholder => Success("awaiting_placeholder", "Session restaurée : placeholder attendu.", session),
            TransferStage.ReadyToPlay => Success("ready_to_play", "Session restaurée : import déjà validé, aucun nouvel import effectué.", session),
            TransferStage.InGame => Success("in_game", "Session restaurée en état InGame. Aucune capture automatique sans confirmation utilisateur.", session),
            TransferStage.Interrupted => Failure("explicit_resume_required", "Session interrompue : une reprise explicite est nécessaire.", session),
            TransferStage.ManualReview => Failure("manual_review", "Session bloquée pour analyse manuelle.", session),
            _ => Success("terminal", "Session terminale.", session)
        };
    }

    private async Task<TransferOperationResult> FailBeforeImportAsync(TransferSession session, string code, string message, CancellationToken cancellationToken)
    {
        if (session.ServerSessionId is Guid serverSessionId && !session.ServerImportStarted)
        {
            try
            {
                await server.AbortSessionAsync(serverSessionId, cancellationToken);
            }
            catch (TransferServerException abortException)
            {
                return await ManualReviewAsync(session, "abort_failed", $"{message} | Impossible de libérer le verrou serveur : {abortException.Message}", cancellationToken);
            }
        }
        session = await PersistAsync(session with
        {
            Stage = TransferStage.Failed,
            ResumeStage = null,
            LastErrorCode = code,
            LastErrorMessage = message
        }, "session-failed-before-import", cancellationToken);
        return Failure(code, message, session);
    }

    private async Task<TransferOperationResult> InterruptAsync(
        TransferSession session,
        TransferStage resumeStage,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        session = await PersistAsync(session with
        {
            Stage = TransferStage.Interrupted,
            ResumeStage = resumeStage,
            LastErrorCode = code,
            LastErrorMessage = message
        }, "session-interrupted", cancellationToken);
        await TryReportFailureAsync(session, code, message, cancellationToken);
        return Failure(code, message, session);
    }

    private async Task<TransferOperationResult> ManualReviewAsync(
        TransferSession session,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        session = await PersistAsync(session with
        {
            Stage = TransferStage.ManualReview,
            ResumeStage = null,
            LastErrorCode = code,
            LastErrorMessage = message
        }, "manual-review-required", cancellationToken);
        await TryReportFailureAsync(session, code, message, cancellationToken);
        return Failure(code, message, session);
    }

    private async Task<TransferOperationResult> RecordNonTerminalErrorAsync(
        TransferSession session,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        session = await PersistAsync(session with { LastErrorCode = code, LastErrorMessage = message }, "non-terminal-error", cancellationToken);
        return Failure(code, message, session);
    }

    private async Task TryHeartbeatAsync(TransferSession session, string clientState, CancellationToken cancellationToken)
    {
        if (session.ServerSessionId is not Guid serverSessionId) return;
        try
        {
            await server.HeartbeatAsync(serverSessionId, clientState, cancellationToken);
        }
        catch (TransferServerException)
        {
            // Un heartbeat ne doit jamais provoquer une écriture WGS ni faire perdre le checkpoint local.
        }
    }

    private async Task TryReportFailureAsync(TransferSession session, string code, string message, CancellationToken cancellationToken)
    {
        if (session.ServerSessionId is not Guid serverSessionId || !session.ServerImportStarted) return;
        try
        {
            await server.ReportFailureAsync(serverSessionId, code, message, cancellationToken);
        }
        catch (TransferServerException)
        {
            // Le journal local reste la source de reprise si le serveur est momentanément inaccessible.
        }
    }

    private async Task<TransferSession?> RequireSessionAsync(Guid localSessionId, CancellationToken cancellationToken) =>
        localSessionId == Guid.Empty ? null : await store.ReadAsync(localSessionId, cancellationToken);

    private async Task<TransferSession> PersistAsync(TransferSession session, string eventName, CancellationToken cancellationToken)
    {
        var updated = session with
        {
            Revision = checked(session.Revision + 1),
            UpdatedAtUtc = _clock.GetUtcNow()
        };
        await store.WriteAsync(updated, eventName, cancellationToken);
        return updated;
    }

    private static PortableSaveArtifact ArtifactFromPath(string path)
    {
        var info = new FileInfo(path);
        return new PortableSaveArtifact(path, string.Empty, info.Exists ? info.Length : 0, null);
    }

    private static string GeneratePlaceholderName() => "GSHIMPORT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static TransferOperationResult Success(string code, string message, TransferSession session) => new(true, code, message, session);
    private static TransferOperationResult Failure(string code, string message, TransferSession? session) => new(false, code, message, session);
}
