using GameSaveHub.Adapters.Abstractions;

namespace GameSaveHub.Client.Orchestration;

public sealed record ManagedSlotBindResult(
    bool Success,
    string Code,
    string Message,
    ManagedSlotBinding? Binding);

public sealed record MaintenanceSafetyStatus(
    bool GameClosed,
    bool NoActiveTransfer,
    bool TransitionIdle,
    bool CheckpointDurable,
    bool SafeToUpdate);

/// <summary>
/// Rattachement explicite d'un slot existant et statut de sûreté pour la mise à jour.
/// Ne mute jamais WGS : seule <see cref="IManagedSlotStore"/> est écrite ici. Le
/// remplacement du contenu du slot reste entièrement porté par <see cref="TransferOrchestrator"/>.
/// </summary>
public sealed class ManagedSlotCoordinator(
    IGameSaveAdapter adapter,
    IManagedSlotStore slotStore,
    ITransferSessionStore sessionStore,
    TransferTransitionGate transitionGate,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<ManagedSlotResolution> GetStatusAsync(string playerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        var binding = await slotStore.ReadAsync(cancellationToken);
        var inspection = await adapter.InspectLocalStorageAsync(cancellationToken);
        return ManagedSlotResolver.Resolve(binding, inspection, inspection.PackageFamilyName, playerName);
    }

    /// <summary>
    /// Doit être appelée sous <see cref="TransferTransitionGate"/> : le contrôle d'ambiguïté
    /// et l'écriture doivent voir le même état WGS qu'aucune autre opération de slot ne modifie
    /// entre-temps.
    /// </summary>
    public async Task<ManagedSlotBindResult> BindExistingAsync(string playerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);

        var existing = await slotStore.ReadAsync(cancellationToken);
        if (existing is not null)
        {
            return new ManagedSlotBindResult(false, "managed_slot_already_bound", "Un slot géré est déjà enregistré sur ce PC.", null);
        }

        var active = await sessionStore.ReadActiveAsync(cancellationToken);
        if (active.Count > 0)
        {
            return new ManagedSlotBindResult(false, "active_session_exists", "Une session locale est active : le rattachement doit attendre sa fin.", null);
        }

        var process = await adapter.DetectGameProcessAsync(cancellationToken);
        if (process.IsRunning)
        {
            return new ManagedSlotBindResult(false, "game_running", "Fermez Planet Crafter avant de rattacher un slot existant.", null);
        }

        var inspection = await adapter.InspectLocalStorageAsync(cancellationToken);
        if (!inspection.Stable)
        {
            return new ManagedSlotBindResult(false, "wgs_not_stable", "La sauvegarde Xbox n'est pas encore stable. Réessayez dans quelques instants.", null);
        }

        var resolution = ManagedSlotResolver.Resolve(null, inspection, inspection.PackageFamilyName, playerName);
        if (resolution.Status is not (ManagedSlotStatus.LegacyCandidate or ManagedSlotStatus.UnboundCandidate) || resolution.Candidate is null)
        {
            return new ManagedSlotBindResult(
                false,
                resolution.SafetyStopCode ?? "managed_slot_bind_unavailable",
                "Aucun slot existant unique ne peut être rattaché automatiquement.",
                null);
        }

        // Réinspection juste avant l'écriture : la fenêtre entre la résolution et l'écriture
        // doit rester la plus courte possible pour ne jamais lier un candidat qui vient de changer.
        var reinspection = await adapter.InspectLocalStorageAsync(cancellationToken);
        var reresolution = ManagedSlotResolver.Resolve(null, reinspection, reinspection.PackageFamilyName, playerName);
        if (reresolution.Status is not (ManagedSlotStatus.LegacyCandidate or ManagedSlotStatus.UnboundCandidate) || reresolution.Candidate is null ||
            !reresolution.Candidate.LogicalName.Equals(resolution.Candidate.LogicalName, StringComparison.OrdinalIgnoreCase))
        {
            return new ManagedSlotBindResult(false, "managed_slot_candidate_changed", "Le slot candidat a changé pendant la vérification. Relancez le rattachement.", null);
        }

        var binding = ManagedSlotBinding.Create(
            adapter.Id,
            reinspection.PackageFamilyName,
            playerName,
            reresolution.Candidate.LogicalName,
            reresolution.Candidate.DisplayName,
            ManagedSlotResolver.PermanentDisplayName,
            _clock.GetUtcNow());
        await slotStore.WriteAsync(binding, cancellationToken);

        return new ManagedSlotBindResult(true, "managed_slot_bound", "Slot existant rattaché.", binding);
    }

    /// <summary>
    /// Strictement en lecture seule : ne déclenche jamais de migration ni d'écriture WGS.
    /// </summary>
    public async Task<MaintenanceSafetyStatus> GetMaintenanceStatusAsync(CancellationToken cancellationToken = default)
    {
        var process = await adapter.DetectGameProcessAsync(cancellationToken);
        var active = await sessionStore.ReadActiveAsync(cancellationToken);
        var gameClosed = !process.IsRunning;
        var noActiveTransfer = active.Count == 0;
        var transitionIdle = !transitionGate.IsBusy;
        var checkpointDurable = !sessionStore.IsWriteInProgress;
        return new MaintenanceSafetyStatus(
            gameClosed,
            noActiveTransfer,
            transitionIdle,
            checkpointDurable,
            gameClosed && noActiveTransfer && transitionIdle && checkpointDurable);
    }
}
