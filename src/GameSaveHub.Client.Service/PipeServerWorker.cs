using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
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
    ILogger<PipeServerWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientServiceOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Le service client nécessite Windows 11.");
        var expectedSid = ResolveSid(_options.RegisteredUserSid);
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = CreatePipe(expectedSid);
            await pipe.WaitForConnectionAsync(stoppingToken);
            try
            {
                var connectedSid = GetConnectedSid(pipe);
                if (!expectedSid.Equals(connectedSid))
                {
                    LogRejectedClient(logger, connectedSid.Value);
                    pipe.Disconnect();
                    continue;
                }
                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPipeFailure(logger, exception);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken);
        var request = line is null ? null : JsonSerializer.Deserialize<PipeRequest>(line, JsonOptions);
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
                "transfer-active" => ToPipe(await GetActiveTransferStatusAsync(cancellationToken)),
                "transfer-start" when request.WorldId is Guid worldId =>
                    await StartTransferAsync(worldId, request.PlayerName, cancellationToken),
                "transfer-placeholder-ready" when request.TransferSessionId is Guid placeholderSessionId =>
                    ToPipe(await orchestrator.ConfirmPlaceholderReadyAsync(placeholderSessionId, cancellationToken)),
                "transfer-play-started" when request.TransferSessionId is Guid playSessionId =>
                    ToPipe(await orchestrator.MarkPlayStartedAsync(playSessionId, cancellationToken)),
                "transfer-play-complete" when request.TransferSessionId is Guid completeSessionId =>
                    ToPipe(await orchestrator.CompletePlayAsync(completeSessionId, cancellationToken)),
                "transfer-resume" when request.TransferSessionId is Guid resumeSessionId =>
                    ToPipe(await orchestrator.ResumeAsync(resumeSessionId, cancellationToken)),
                "transfer-abort" when request.TransferSessionId is Guid abortSessionId =>
                    ToPipe(await orchestrator.AbortAsync(abortSessionId, cancellationToken)),
                _ => new PipeResponse(false, "command_unknown", "Commande locale inconnue ou incomplète.")
            };
        }
        catch (TransferServerException exception)
        {
            return new PipeResponse(false, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new PipeResponse(false, "operation_failed", exception.Message);
        }
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

        return ToPipe(await orchestrator.StartAsync(worldId, state.RegisteredPlayerName, cancellationToken));
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
}
