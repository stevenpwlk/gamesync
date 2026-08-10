using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Client.Service;

public sealed partial class PipeServerWorker(
    IOptions<ClientServiceOptions> options,
    ClientStateStore stateStore,
    DeviceIdentity identity,
    ServerEnrollmentClient enrollmentClient,
    ITransferServerClient serverClient,
    TransferOrchestrator orchestrator,
    TransferTransitionGate transitionGate,
    ITransferSessionStore sessionStore,
    IGameSaveAdapter adapter,
    ManagedSlotCoordinator slotCoordinator,
    ILogger<PipeServerWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientServiceOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Le service client nécessite Windows 11.");
        var expectedSid = ResolveSid(_options.RegisteredUserSid);
        PreloadIdentityAssemblies();
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = CreatePipe(expectedSid);
            await pipe.WaitForConnectionAsync(stoppingToken);
            try
            {
                await HandleClientAsync(pipe, expectedSid, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPipeFailure(logger, exception);
            }
        }
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        SecurityIdentifier expectedSid,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        // La lecture doit précéder le contrôle d'identité : Windows refuse
        // ImpersonateNamedPipeClient tant qu'aucune donnée n'a été lue sur le canal.
        // Vérifier le SID avant de lire faisait échouer *toutes* les connexions avec
        // « Impossible d'emprunter une identité [...] tant qu'aucune donnée n'a été lue ».
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null) return;

        // Rien n'a encore été exécuté à ce stade : la requête n'est même pas désérialisée.
        // Le contrôle du SID reste donc bien antérieur à toute action.
        var connectedSid = GetConnectedSid(pipe);
        if (!expectedSid.Equals(connectedSid))
        {
            LogRejectedClient(logger, connectedSid.Value);
            var refusal = new PipeResponse(
                false,
                "client_not_authorized",
                "Ce compte Windows n'est pas celui enregistré pour GameSave Hub sur ce PC.");
            await writer.WriteLineAsync(JsonSerializer.Serialize(refusal, JsonOptions).AsMemory(), cancellationToken);
            return;
        }

        var request = JsonSerializer.Deserialize<PipeRequest>(line, JsonOptions);
        var response = request is null
            ? new PipeResponse(false, "invalid_request", "Requête locale invalide.")
            : await DispatchAsync(request, cancellationToken);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions).AsMemory(), cancellationToken);
    }

    private async Task<PipeResponse> DispatchAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Command.ToLowerInvariant() switch
            {
                "status" => new PipeResponse(true, "ok", "État client.", new
                {
                    State = await stateStore.ReadAsync(cancellationToken),
                    KeyExists = identity.Exists,
                    WgsTransferEnabled = _options.EnableWgsTransfer,
                    ActiveTransfers = await orchestrator.GetActiveSessionsAsync(cancellationToken)
                }),
                "server-health" => new PipeResponse(true, "ok", "État serveur.", new
                {
                    Healthy = await enrollmentClient.IsServerHealthyAsync(cancellationToken)
                }),
                "home-context" => await BuildHomeContextAsync(cancellationToken),
                "enroll" when !string.IsNullOrWhiteSpace(request.EnrollmentCode) &&
                              !string.IsNullOrWhiteSpace(request.DeviceName) &&
                              !string.IsNullOrWhiteSpace(request.PlayerName) =>
                    new PipeResponse(
                        true,
                        "ok",
                        "Appareil enrôlé et pseudo local enregistré.",
                        await enrollmentClient.EnrollAsync(
                            request.EnrollmentCode,
                            request.DeviceName,
                            request.PlayerName,
                            cancellationToken)),
                "profile-player-set" when !string.IsNullOrWhiteSpace(request.PlayerName) =>
                    await SetPlayerProfileAsync(request.PlayerName, cancellationToken),
                "world-list" => new PipeResponse(
                    true,
                    "ok",
                    "Catalogue des mondes.",
                    await serverClient.ListWorldsAsync(cancellationToken)),
                "world-preview" when request.WorldId is Guid previewWorldId =>
                    new PipeResponse(
                        true,
                        "ok",
                        "Aperçu du monde.",
                        await serverClient.GetWorldPreviewAsync(previewWorldId, cancellationToken)),
                "preflight" when request.WorldId is Guid preflightWorldId =>
                    await RunPreflightAsync(preflightWorldId, cancellationToken),
                "transfer-active" => await GetTransferScreenAsync(cancellationToken),
                "diagnostic-report" => await BuildDiagnosticReportAsync(cancellationToken),
                "transfer-start" when request.WorldId is Guid worldId =>
                    await transitionGate.RunAsync(
                        () => StartTransferAsync(worldId, request.PlayerName, cancellationToken),
                        cancellationToken),
                "transfer-placeholder-ready" when request.TransferSessionId is Guid placeholderSessionId =>
                    ToPipe(await transitionGate.RunAsync(
                        () => orchestrator.ConfirmPlaceholderReadyAsync(placeholderSessionId, cancellationToken),
                        cancellationToken)),
                "transfer-play-started" when request.TransferSessionId is Guid playSessionId =>
                    ToPipe(await transitionGate.RunAsync(
                        () => orchestrator.MarkPlayStartedAsync(playSessionId, cancellationToken),
                        cancellationToken)),
                "transfer-play-complete" when request.TransferSessionId is Guid completeSessionId =>
                    ToPipe(await transitionGate.RunAsync(
                        () => orchestrator.CompletePlayAsync(completeSessionId, cancellationToken),
                        cancellationToken)),
                "transfer-resume" when request.TransferSessionId is Guid resumeSessionId =>
                    ToPipe(await transitionGate.RunAsync(
                        () => orchestrator.ResumeAsync(resumeSessionId, cancellationToken),
                        cancellationToken)),
                "transfer-abort" when request.TransferSessionId is Guid abortSessionId =>
                    ToPipe(await transitionGate.RunAsync(
                        () => orchestrator.AbortAsync(abortSessionId, cancellationToken),
                        cancellationToken)),
                "managed-slot-bind-existing" =>
                    await transitionGate.RunAsync(() => BindExistingManagedSlotAsync(cancellationToken), cancellationToken),
                "maintenance-status" => new PipeResponse(
                    true,
                    "ok",
                    "État de sûreté pour mise à jour.",
                    await slotCoordinator.GetMaintenanceStatusAsync(cancellationToken)),
                _ => new PipeResponse(false, "command_unknown", "Commande locale inconnue ou incomplète.")
            };
        }
        catch (TransferServerException exception)
        {
            return new PipeResponse(false, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            LogOperationFailure(logger, request.Command, exception);
            return new PipeResponse(false, "operation_failed", "L'opération n'a pas pu aboutir. Les détails sont disponibles dans le diagnostic.");
        }
    }

    private async Task<PipeResponse> BuildHomeContextAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.ReadAsync(cancellationToken);
        var sessions = await orchestrator.GetAllSessionsAsync(cancellationToken);
        var active = sessions.Where(item => TransferStageRules.HoldsLocalLock(item.Stage)).ToArray();
        var lastFinished = sessions
            .Where(item => TransferStageRules.IsTerminal(item.Stage))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault();

        var serverHealthy = await enrollmentClient.IsServerHealthyAsync(cancellationToken);
        WorldCatalogItemResponse? primaryWorld = null;
        WorldStatusResponse? worldStatus = null;
        string? safetyStopCode = active.Length > 1 ? "multiple_local_sessions" : null;

        if (serverHealthy && state.DeviceId is not null)
        {
            var selection = PrimaryWorldSelector.Select(await serverClient.ListWorldsAsync(cancellationToken));
            if (selection.Success)
            {
                primaryWorld = selection.World;
                worldStatus = await serverClient.GetWorldStatusAsync(primaryWorld!.WorldId, cancellationToken);
            }
            else
            {
                safetyStopCode = selection.Code;
            }
        }

        var activeLocal = active.Length == 1 ? active[0] : null;
        if (safetyStopCode is null && state.DeviceId is Guid localDeviceId)
        {
            safetyStopCode = HomeContextConsistency.Validate(localDeviceId, worldStatus, activeLocal);
            if (safetyStopCode is null && activeLocal is not null && primaryWorld is not null &&
                activeLocal.WorldId != primaryWorld.WorldId)
            {
                safetyStopCode = "local_world_mismatch";
            }
        }

        var process = await adapter.DetectGameProcessAsync(cancellationToken);
        var installation = await adapter.DetectInstallationAsync(cancellationToken);
        var wgsAvailable = installation.WgsRoot is not null;
        var wgsStable = false;
        var managedSlotStatus = ManagedSlotStatus.Ready;
        if (wgsAvailable && state.DeviceId is not null && active.Length == 0 && !process.IsRunning)
        {
            try
            {
                wgsStable = (await adapter.InspectLocalStorageAsync(cancellationToken)).Stable;
                if (wgsStable && !string.IsNullOrWhiteSpace(state.RegisteredPlayerName))
                {
                    managedSlotStatus = (await slotCoordinator.GetStatusAsync(state.RegisteredPlayerName, cancellationToken)).Status;
                }
            }
            catch (InvalidOperationException)
            {
                // L'accueil et l'onboarding restent disponibles si WGS n'existe pas encore.
            }
        }
        var context = new HomeContextSnapshot(
            state.DeviceId is not null,
            state.DeviceId,
            state.RegisteredPlayerName,
            serverHealthy,
            primaryWorld,
            worldStatus,
            safetyStopCode,
            activeLocal,
            lastFinished,
            process.IsRunning,
            wgsStable,
            wgsAvailable,
            managedSlotStatus);

        return new PipeResponse(true, "ok", "Contexte d'accueil.", context);
    }

    private async Task<PipeResponse> SetPlayerProfileAsync(string playerName, CancellationToken cancellationToken)
    {
        var active = await orchestrator.GetActiveSessionsAsync(cancellationToken);
        if (active.Count != 0)
        {
            return new PipeResponse(
                false,
                "active_transfer_exists",
                "Le pseudo local ne peut pas être modifié pendant une session de transfert.");
        }

        var state = await stateStore.ReadAsync(cancellationToken);
        var updated = state with
        {
            SchemaVersion = 2,
            RegisteredPlayerName = playerName.Trim()
        };
        await stateStore.WriteAsync(updated, cancellationToken);
        return new PipeResponse(true, "ok", "Pseudo Planet Crafter enregistré.", updated);
    }

    private async Task<PipeResponse> RunPreflightAsync(Guid worldId, CancellationToken cancellationToken)
    {
        var state = await stateStore.ReadAsync(cancellationToken);
        if (state.DeviceId is null)
        {
            return new PipeResponse(false, "device_not_enrolled", "Ce PC doit être associé au serveur avant la vérification.");
        }

        var preview = await serverClient.GetWorldPreviewAsync(worldId, cancellationToken);
        if (!preview.HasArtifact)
        {
            return new PipeResponse(false, "world_has_no_artifact", "Ce monde serveur ne possède encore aucune sauvegarde.", preview);
        }

        var compatibility = PlayerCompatibilityRules.Evaluate(state.RegisteredPlayerName, preview.Players);
        return new PipeResponse(
            compatibility.Compatible,
            compatibility.Outcome switch
            {
                PlayerCompatibilityOutcome.Compatible => "preflight_ready",
                PlayerCompatibilityOutcome.PlayerNameMissing => "player_profile_missing",
                PlayerCompatibilityOutcome.PlayerNotFound => "player_not_found",
                PlayerCompatibilityOutcome.PlayerAmbiguous => "player_ambiguous",
                _ => "preflight_failed"
            },
            compatibility.Message,
            new
            {
                Preview = preview,
                Compatibility = compatibility,
                WgsTransferEnabled = _options.EnableWgsTransfer
            });
    }

    private async Task<PipeResponse> StartTransferAsync(
        Guid worldId,
        string? requestedPlayerName,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableWgsTransfer)
        {
            return new PipeResponse(
                false,
                "local_transfer_gate_closed",
                "La Phase 3 est en mode préflight : l'écriture WGS est désactivée localement.");
        }

        var preflight = await RunPreflightAsync(worldId, cancellationToken);
        if (!preflight.Success) return preflight;

        var localStorage = await adapter.InspectLocalStorageAsync(cancellationToken);
        if (localStorage.GameRunning || !localStorage.Stable)
        {
            return new PipeResponse(
                false,
                "wgs_not_stable",
                "La sauvegarde Xbox n'est pas encore stable. Fermez le jeu et patientez quelques instants.");
        }

        var state = await stateStore.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(state.RegisteredPlayerName))
        {
            return new PipeResponse(false, "player_profile_missing", "Pseudo Planet Crafter non configuré.");
        }
        if (!string.IsNullOrWhiteSpace(requestedPlayerName) &&
            !requestedPlayerName.Trim().Equals(state.RegisteredPlayerName, StringComparison.OrdinalIgnoreCase))
        {
            return new PipeResponse(
                false,
                "player_profile_mismatch",
                "Le pseudo demandé ne correspond pas au profil local enregistré.");
        }

        var resolution = await slotCoordinator.GetStatusAsync(state.RegisteredPlayerName, cancellationToken);
        var flowKind = resolution.Status switch
        {
            ManagedSlotStatus.Missing => TransferFlowKind.InitialSlotSetup,
            // RenamePending : slot rattaché, nom affiché WGS pas encore renommé. La
            // réutilisation écrit ce nom en même temps que le contenu (voir ManagedSlotResolver
            // et HomeStatePresenter.PresentSlotStatus pour la même règle côté accueil).
            ManagedSlotStatus.Ready or ManagedSlotStatus.RenamePending => TransferFlowKind.ManagedSlotReuse,
            _ => (TransferFlowKind?)null
        };
        if (flowKind is null)
        {
            return new PipeResponse(
                false,
                resolution.SafetyStopCode ?? "managed_slot_requires_attention",
                "Le slot du monde partagé doit être vérifié avant de continuer.");
        }

        return ToPipe(await orchestrator.StartAsync(worldId, state.RegisteredPlayerName, flowKind.Value, cancellationToken));
    }

    private async Task<PipeResponse> BindExistingManagedSlotAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(state.RegisteredPlayerName))
        {
            return new PipeResponse(false, "player_profile_missing", "Pseudo Planet Crafter non configuré.");
        }

        var result = await slotCoordinator.BindExistingAsync(state.RegisteredPlayerName, cancellationToken);
        return new PipeResponse(result.Success, result.Code, result.Message);
    }

    /// <summary>
    /// Rapport technique destiné à être renvoyé après une session, réussie ou non.
    /// </summary>
    /// <remarks>
    /// Le PC distant ne peut être sollicité qu'une fois : ce rapport doit donc contenir
    /// tout ce qui permettrait de diagnostiquer un échec sans rappeler le joueur.
    /// Il ne contient en revanche <b>aucune donnée de sauvegarde</b> : les répertoires
    /// <c>inbound</c>, <c>prepared</c>, <c>outbound</c> et <c>safety</c> ne sont jamais
    /// lus, seuls leurs chemins et empreintes déjà présents dans le checkpoint le sont.
    /// </remarks>
    private async Task<PipeResponse> BuildDiagnosticReportAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.ReadAsync(cancellationToken);
        var sessions = await orchestrator.GetAllSessionsAsync(cancellationToken);

        var journals = new List<object>();
        foreach (var session in sessions)
        {
            var journalPath = Path.Combine(sessionStore.GetSessionDirectory(session.LocalSessionId), "events.ndjson");
            string[] entries;
            try
            {
                entries = File.Exists(journalPath)
                    ? await File.ReadAllLinesAsync(journalPath, cancellationToken)
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                entries = [$"journal illisible : {exception.Message}"];
            }

            journals.Add(new
            {
                session.LocalSessionId,
                Stage = session.Stage.ToString(),
                Events = entries.TakeLast(200).ToArray()
            });
        }

        InstallationDetection? detection = null;
        string? detectionError = null;
        try
        {
            detection = await adapter.DetectInstallationAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            detectionError = exception.Message;
        }

        return new PipeResponse(true, "ok", "Rapport de diagnostic généré.", new
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Machine = Environment.MachineName,
            OperatingSystem = Environment.OSVersion.VersionString,
            Runtime = Environment.Version.ToString(),
            Client = new
            {
                state.DeviceId,
                state.RegisteredPlayerName,
                IdentityKeyExists = identity.Exists
            },
            Configuration = new
            {
                _options.PipeName,
                _options.ServerBaseUrl,
                _options.RegisteredUserSid,
                _options.RegisteredUserLocalAppData,
                _options.TransferRootPath,
                _options.EnableWgsTransfer
            },
            Adapter = new
            {
                adapter.Capabilities,
                Detection = detection,
                DetectionError = detectionError
            },
            Sessions = sessions,
            Journals = journals,
            WindowsEventLog = ReadOwnEventLog()
        });
    }

    /// <summary>
    /// Dernières entrées que ce service a écrites dans le journal Windows.
    /// C'est là que remontent les échecs du canal nommé, invisibles ailleurs.
    /// </summary>
    private static string[] ReadOwnEventLog()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try
        {
            using var log = new System.Diagnostics.EventLog("Application");
            var since = DateTime.Now.AddDays(-7);
            return log.Entries
                .Cast<System.Diagnostics.EventLogEntry>()
                .Where(entry => entry.TimeGenerated >= since &&
                                entry.Source.StartsWith("GameSaveHub", StringComparison.OrdinalIgnoreCase))
                .TakeLast(60)
                .Select(entry => $"{entry.TimeGenerated:O} [{entry.EntryType}] {entry.Message}")
                .ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Security.SecurityException or ArgumentException)
        {
            return [$"journal Windows illisible : {exception.Message}"];
        }
    }

    /// <summary>
    /// Tout ce dont l'écran de transfert a besoin : la session active s'il y en a une,
    /// et dans tous les cas le dernier transfert achevé.
    /// </summary>
    /// <remarks>
    /// Le dernier résultat est renvoyé même lorsqu'aucune session n'est active, parce
    /// qu'une session peut se terminer sans que le joueur soit devant l'écran : le
    /// service reprend seul certaines étapes à son démarrage.
    /// </remarks>
    private async Task<PipeResponse> GetTransferScreenAsync(CancellationToken cancellationToken)
    {
        var result = await GetActiveTransferStatusAsync(cancellationToken);
        var lastFinished = (await orchestrator.GetAllSessionsAsync(cancellationToken))
            .Where(item => TransferStageRules.IsTerminal(item.Stage))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault();

        var session = result.Session;
        return new PipeResponse(result.Success, result.Code, result.Message, new
        {
            Session = session,
            Stage = session?.Stage.ToString(),
            PlaceholderName = session?.PlaceholderName,
            LastFinished = lastFinished
        });
    }

    private async Task<TransferOperationResult> GetActiveTransferStatusAsync(CancellationToken cancellationToken)
    {
        var active = await orchestrator.GetActiveSessionsAsync(cancellationToken);
        if (active.Count == 0) return new TransferOperationResult(true, "no_active_transfer", "Aucune session de transfert active.", null);
        if (active.Count > 1) return new TransferOperationResult(false, "multiple_active_transfers", "Plusieurs sessions actives sont présentes : analyse manuelle requise.", active[0]);
        return new TransferOperationResult(true, "active_transfer", "Session de transfert active.", active[0]);
    }

    private static PipeResponse ToPipe(TransferOperationResult result)
    {
        var session = result.Session;
        return new PipeResponse(
            result.Success,
            result.Code,
            result.Message,
            session is null ? null : new
            {
                Session = session,
                Stage = session.Stage.ToString(),
                session.PlaceholderName
            });
    }

    private NamedPipeServerStream CreatePipe(SecurityIdentifier userSid)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(userSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            _options.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            65536,
            65536,
            security);
    }

    /// <summary>
    /// Force le chargement des assemblies d'identité avant toute usurpation.
    /// </summary>
    /// <remarks>
    /// Le service est publié en exécutable mono-fichier. Une assembly chargée
    /// paresseusement pour la première fois <em>à l'intérieur</em> de
    /// <see cref="NamedPipeServerStream.RunAsClient"/> échoue avec
    /// <c>FileNotFoundException: System.Security.Claims</c> : la résolution
    /// intervient alors que le thread porte le jeton du client.
    /// <see cref="WindowsIdentity"/> dérivant de <c>ClaimsIdentity</c>, le simple fait
    /// d'y toucher ici, dans le contexte du service, suffit à ce que le type soit déjà
    /// résolu au moment du contrôle d'identité.
    /// </remarks>
    private static void PreloadIdentityAssemblies()
    {
        using var current = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        _ = current.User;
        _ = typeof(System.Security.Claims.ClaimsIdentity).Assembly.FullName;
    }

    private static SecurityIdentifier GetConnectedSid(NamedPipeServerStream pipe)
    {
        SecurityIdentifier? sid = null;
        pipe.RunAsClient(() => sid = WindowsIdentity.GetCurrent(TokenAccessLevels.Query).User);
        return sid ?? throw new InvalidOperationException("SID du client local introuvable.");
    }

    private static SecurityIdentifier ResolveSid(string configuredSid)
    {
        if (string.IsNullOrWhiteSpace(configuredSid))
        {
            throw new InvalidOperationException(
                "ClientService:RegisteredUserSid est obligatoire. Le service ne doit jamais utiliser le SID LocalSystem comme substitut au compte joueur.");
        }
        return new SecurityIdentifier(configuredSid);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connexion pipe refusée pour le SID {Sid}.")]
    private static partial void LogRejectedClient(ILogger logger, string sid);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec du serveur named pipe.")]
    private static partial void LogPipeFailure(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de la commande locale {Command}.")]
    private static partial void LogOperationFailure(ILogger logger, string command, Exception exception);
}
