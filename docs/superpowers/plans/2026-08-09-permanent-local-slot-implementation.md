# Permanent Local Slot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Faire de `GSH-MONDE-PARTAGE` un slot WGS permanent, créé une seule fois par PC puis réutilisé sans doublon, avec une application Lot 2 entièrement fonctionnelle et prête à servir de socle au Lot 3.

**Architecture:** L’identité durable du slot vit dans un fichier atomique séparé sous `ProgramData`. Le service dérive un statut de slot pur, l’orchestrateur choisit entre configuration initiale et réutilisation, et l’adaptateur ajoute une voie de remplacement dédiée qui protège tous les autres mondes sans affaiblir l’import placeholder historique. L’API reçoit en parallèle une barrière de version additive afin qu’un ancien client ne puisse plus acquérir le monde après activation du nouveau socle.

**Tech Stack:** .NET 10, C# 14, WPF, service Windows, named pipes ACL, System.Text.Json, ASP.NET Core minimal API, xUnit, PowerShell, WGS Xbox/Game Pass.

## Global Constraints

- Plateforme : Windows 11 x64 et The Planet Crafter Xbox/Game Pass uniquement.
- Monde visible permanent : `GSH-MONDE-PARTAGE`.
- Un seul monde partagé principal côté Hub.
- Le nom logique WGS est l’identité d’accès ; le nom visible n’est jamais une clé unique.
- `managed-slot.json` est séparé de `client-state.json` et stocké sous `%ProgramData%\GameSaveHub`.
- Aucune suppression automatique de sauvegarde locale.
- Aucune écriture WGS si le jeu tourne, si WGS est instable, si l’identité du slot est ambiguë ou si une autre transition est active.
- Chaque écriture conserve baseline, snapshot préalable, validation juste avant écriture, validation finale, rollback et réconciliation idempotente.
- Le chemin placeholder existant reste strict : il continue d’exiger un nouveau `Standard-X` et n’est pas assoupli.
- Le parcours quotidien ne crée aucun monde et ne demande qu’un lancement du jeu après `Prendre la main`.
- La configuration initiale annonce explicitement ses deux lancements exceptionnels.
- Les contrats réseau évoluent de manière additive.
- Aucun déploiement NAS/API, installation pilote, élévation de version minimale ou écriture WGS réelle sans approbation explicite.
- Le travail reste sur `codex/v1-lot2-contextual-app` jusqu’à validation conjointe ; aucun push ni fusion vers `main`.
- Le plancher automatisé est 202 tests réussis ; le total ne doit jamais diminuer.
- Le Lot 2 n’est déclaré terminé qu’après deux réutilisations du même slot sur Steven et le cycle réel `Steven → Bob → Steven` quand Bob est disponible.

---

## File Map

### New focused units

- `src/GameSaveHub.Client.Orchestration/ManagedSlotModels.cs` — binding durable, statuts et vues non-WPF.
- `src/GameSaveHub.Client.Orchestration/IManagedSlotStore.cs` — contrat de persistance du binding.
- `src/GameSaveHub.Client.Orchestration/FileManagedSlotStore.cs` — lecture/écriture atomique de `managed-slot.json`.
- `src/GameSaveHub.Client.Orchestration/ManagedSlotResolver.cs` — résolution pure binding + inventaire WGS.
- `src/GameSaveHub.Client.Service/ManagedSlotCoordinator.cs` — rattachement explicite et orchestration service.
- `src/GameSaveHub.Contracts/ClientCompatibilityPolicy.cs` — comparaison de versions pure et testable.
- `tests/Unit/ManagedSlotStoreTests.cs` — persistance et compatibilité de schéma.
- `tests/Unit/ManagedSlotResolverTests.cs` — table de décision du slot.
- `tests/Unit/ManagedSlotCoordinatorTests.cs` — rattachement et refus sûrs.
- `tests/Unit/ClientCompatibilityPolicyTests.cs` — barrière de version.

### Existing files changed by responsibility

- `src/GameSaveHub.Adapters.Abstractions/IGameSaveAdapter.cs` — nouveaux contrats de baseline/remplacement permanent.
- `src/GameSaveHub.Contracts/DiagnosticsContracts.cs` — résultats du remplacement permanent.
- `src/GameSaveHub.Adapters.PlanetCrafter.GamePass/PlanetCrafterWorldTransformer.cs` — renommage sémantique sûr.
- `src/GameSaveHub.Adapters.PlanetCrafter.GamePass/PlanetCrafterGamePassAdapter.cs` — baseline, import et réconciliation du slot permanent.
- `src/GameSaveHub.Client.Orchestration/TransferModels.cs` — mode de session additif et checkpoints du binding.
- `src/GameSaveHub.Client.Orchestration/TransferOrchestrator.cs` — branche initiale/réutilisation et commit durable du binding.
- `src/GameSaveHub.Client.Orchestration/HomeContextModels.cs` — contexte du slot.
- `src/GameSaveHub.Client.Orchestration/HomeStatePresenter.cs` — états et actions de configuration/rattachement.
- `src/GameSaveHub.Client.Orchestration/TransferTransitionGate.cs` — état de sûreté observable par l’updater.
- `src/GameSaveHub.Client.Service/ClientServiceOptions.cs` — chemin `ManagedSlotStatePath`.
- `src/GameSaveHub.Client.Service/Program.cs` — injection du store et du coordinateur.
- `src/GameSaveHub.Client.Service/PipeServerWorker.cs` — commandes, contexte et statut de maintenance.
- `src/GameSaveHub.Client.Service/AuthenticatedTransferServerClient.cs` — en-tête de version.
- `src/GameSaveHub.Client.App/MainWindow.xaml` — panneau nom/copie et états de configuration.
- `src/GameSaveHub.Client.App/MainWindow.xaml.cs` — actions minces WPF.
- `src/GameSaveHub.Server.Api/ApiOptions.cs` — version minimale d’acquisition.
- `src/GameSaveHub.Server.Api/Program.cs` — refus `client_update_required`.
- `tools/INSTALL-GAMESAVEHUB-CLIENT.ps1` — configuration du nouveau chemin persistant.
- `tools/build-integrated-phase3.ps1` — package pilote `0.4.0` et contrôles de contenu.
- `SOURCE-SHA256SUMS.txt` — hashes des sources livrées.
- `docs/operations/LOT2-CONTEXTUAL-CLIENT.md` — comportement final.
- `docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md` — suivi fait/reste à faire.

---

### Task 1: Persist the managed-slot binding separately

**Files:**
- Create: `src/GameSaveHub.Client.Orchestration/ManagedSlotModels.cs`
- Create: `src/GameSaveHub.Client.Orchestration/IManagedSlotStore.cs`
- Create: `src/GameSaveHub.Client.Orchestration/FileManagedSlotStore.cs`
- Create: `tests/Unit/ManagedSlotStoreTests.cs`

**Interfaces:**
- Produces: `ManagedSlotBinding`, `IManagedSlotStore.ReadAsync`, `IManagedSlotStore.WriteAsync`.
- Consumes: filesystem only; no adapter or server dependency.

`ManagedSlotBinding` is a schema-versioned record with a single public factory:

```csharp
public static ManagedSlotBinding Create(
    string adapterId,
    string packageFamilyName,
    string playerName,
    string logicalName,
    string observedDisplayName,
    string expectedDisplayName,
    DateTimeOffset boundAtUtc);
```

- [ ] **Step 1: Write failing persistence tests**

```csharp
[Fact]
public async Task WriteThenReadPreservesBindingAtomically()
{
    var store = new FileManagedSlotStore(Path.Combine(_root, "managed-slot.json"));
    var binding = ManagedSlotBinding.Create(
        "planet-crafter-pc-gamepass", "MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t",
        "Stevenpwlk", "Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE",
        DateTimeOffset.Parse("2026-08-09T15:00:00Z"));

    await store.WriteAsync(binding);

    Assert.Equal(binding, await store.ReadAsync());
    Assert.Empty(Directory.GetFiles(_root, "*.tmp-*"));
}

[Fact]
public async Task ReadRejectsUnsupportedSchemaWithoutRewritingFile()
{
    var path = Path.Combine(_root, "managed-slot.json");
    await File.WriteAllTextAsync(path, "{\"schemaVersion\":99}");
    var before = await File.ReadAllBytesAsync(path);

    await Assert.ThrowsAsync<InvalidDataException>(() => new FileManagedSlotStore(path).ReadAsync());

    Assert.Equal(before, await File.ReadAllBytesAsync(path));
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter FullyQualifiedName~ManagedSlotStoreTests`

Expected: FAIL because the binding and store do not exist.

- [ ] **Step 3: Implement the minimal schema and atomic store**

```csharp
public sealed record ManagedSlotBinding(
    int SchemaVersion,
    string AdapterId,
    string PackageFamilyName,
    string PlayerName,
    string LogicalName,
    string CurrentDisplayName,
    string DesiredDisplayName,
    DateTimeOffset BoundAtUtc,
    DateTimeOffset LastValidatedAtUtc,
    IReadOnlyList<DiscoveredPlayer> LastValidatedPlayers)
{
    public const int CurrentSchemaVersion = 1;
}

public interface IManagedSlotStore
{
    Task<ManagedSlotBinding?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(ManagedSlotBinding binding, CancellationToken cancellationToken = default);
}
```

Implement `FileManagedSlotStore` with `FileMode.CreateNew`, `FileOptions.WriteThrough`, `Flush(true)` and `File.Move(temporary, path, overwrite: true)`. Never expose a delete method.

- [ ] **Step 4: Run focused and full tests**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter FullyQualifiedName~ManagedSlotStoreTests`

Run: `dotnet test GameSaveHub.slnx --no-restore --nologo`

Expected: focused PASS; full total at least 204 and 0 failures.

- [ ] **Step 5: Commit**

```powershell
git add src/GameSaveHub.Client.Orchestration/ManagedSlotModels.cs src/GameSaveHub.Client.Orchestration/IManagedSlotStore.cs src/GameSaveHub.Client.Orchestration/FileManagedSlotStore.cs tests/Unit/ManagedSlotStoreTests.cs
git commit -m "feat: persist permanent managed slot"
```

### Task 2: Resolve slot identity without ambiguity

**Files:**
- Create: `src/GameSaveHub.Client.Orchestration/ManagedSlotResolver.cs`
- Create: `tests/Unit/ManagedSlotResolverTests.cs`
- Modify: `src/GameSaveHub.Client.Orchestration/ManagedSlotModels.cs`

**Interfaces:**
- Consumes: `ManagedSlotBinding?`, `LocalStorageInspection`, adapter/package/player identity.
- Produces: `ManagedSlotResolution Resolve(ManagedSlotBinding? binding, LocalStorageInspection inspection, string packageFamilyName, string playerName)` with no I/O.

- [ ] **Step 1: Write the decision-table tests**

```csharp
[Theory]
[InlineData(false, 0, 0, ManagedSlotStatus.Missing)]
[InlineData(false, 1, 0, ManagedSlotStatus.UnboundCandidate)]
[InlineData(false, 0, 1, ManagedSlotStatus.LegacyCandidate)]
[InlineData(false, 2, 0, ManagedSlotStatus.Ambiguous)]
public void ResolveUnboundInventory(bool hasBinding, int desiredCount, int legacyCount, ManagedSlotStatus expected)
{
    var inspection = InspectionWith(desiredCount, legacyCount);
    var result = ManagedSlotResolver.Resolve(hasBinding ? Binding() : null, inspection, Package, Player);
    Assert.Equal(expected, result.Status);
}

[Fact]
public void BoundLogicalNameWinsOverVisibleHomonym()
{
    var result = ManagedSlotResolver.Resolve(
        Binding(logicalName: "Standard-5.json"),
        Inspection(World("Standard-5.json", "GSH-MONDE-PARTAGE"), World("Standard-8.json", "GSH-MONDE-PARTAGE")),
        Package, Player);

    Assert.Equal(ManagedSlotStatus.Ready, result.Status);
    Assert.Equal("Standard-5.json", result.Candidate!.LogicalName);
}
```

Also cover package mismatch, player mismatch, missing logical name, unexpected display name, invalid host topology and legacy `GSH-SHLAGS-RETURN`.

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter FullyQualifiedName~ManagedSlotResolverTests`

Expected: FAIL because resolver/status types are missing.

- [ ] **Step 3: Implement the pure resolver**

```csharp
public enum ManagedSlotStatus
{
    Missing, Ready, RenamePending, UnboundCandidate, LegacyCandidate,
    BoundSlotMissing, BindingMismatch, InvalidTopology, Ambiguous
}

public sealed record ManagedSlotResolution(
    ManagedSlotStatus Status,
    DiscoveredWorld? Candidate,
    string? SafetyStopCode);
```

Resolution order: validate binding identity; select by logical name when bound; otherwise count exact desired-name candidates and then legacy candidates; validate one player `Id == 0 && IsHost` whose normalized name equals the registered player. Never return the logical name in a user-facing message.

- [ ] **Step 4: Run focused/full tests and commit**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter FullyQualifiedName~ManagedSlotResolverTests`

Run: `dotnet test GameSaveHub.slnx --no-restore --nologo`

```powershell
git add src/GameSaveHub.Client.Orchestration/ManagedSlotModels.cs src/GameSaveHub.Client.Orchestration/ManagedSlotResolver.cs tests/Unit/ManagedSlotResolverTests.cs
git commit -m "feat: resolve managed slot identity safely"
```

### Task 3: Prepare host artifacts with the permanent display name

**Files:**
- Modify: `src/GameSaveHub.Contracts/DiagnosticsContracts.cs`
- Modify: `src/GameSaveHub.Adapters.Abstractions/IGameSaveAdapter.cs`
- Modify: `src/GameSaveHub.Adapters.PlanetCrafter.GamePass/PlanetCrafterWorldTransformer.cs`
- Modify: `src/GameSaveHub.Adapters.PlanetCrafter.GamePass/PlanetCrafterGamePassAdapter.cs`
- Modify: `tests/Unit/PlanetCrafterGamePassAdapterTests.cs`
- Modify: adapter fakes in `tests/Unit/TransferOrchestratorTests.cs`, `tests/Unit/SaveExporterServiceTests.cs`

**Interfaces:**
- Changes: `PrepareForHostAsync(artifact, playerName, targetDisplayName, outputRoot, cancellationToken)`.
- Produces: a validated artifact whose payload and manifest both use `targetDisplayName`.

- [ ] **Step 1: Add failing semantic rename tests**

```csharp
[Fact]
public async Task PrepareForHostSetsPermanentDisplayNameInPayloadAndManifest()
{
    var prepared = await _adapter.PrepareForHostAsync(
        SourceArtifact(), "Stevenpwlk", "GSH-MONDE-PARTAGE", _output);

    Assert.True(prepared.Success);
    Assert.Equal("GSH-MONDE-PARTAGE", prepared.PreparedArtifact!.Manifest!.DisplayName);
    Assert.True((await _adapter.ValidateArtifactAsync(prepared.PreparedArtifact)).IsValid);
}

[Theory]
[InlineData("")]
[InlineData("bad\rname")]
[InlineData("bad\nname")]
public async Task PrepareForHostRejectsUnsafeDisplayName(string name)
{
    var prepared = await _adapter.PrepareForHostAsync(
        SourceArtifact(), "Stevenpwlk", name, _output);

    Assert.False(prepared.Success);
    Assert.Equal(HostPreparationOutcome.InvalidDisplayName, prepared.Outcome);
    Assert.Contains("invalid_target_display_name", prepared.Errors);
    Assert.Null(prepared.PreparedArtifact);
}
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter "FullyQualifiedName~PrepareForHost"`

Expected: compile failure for the new signature or assertion failure because the display name is unchanged.

- [ ] **Step 3: Implement one semantic transformation**

Add the enum value `HostPreparationOutcome.InvalidDisplayName`. Extend `PlanetCrafterWorldTransformer.PrepareForHost` with `targetDisplayName`. Validate trimmed ASCII/control-free length `1..64`, return the stable error `invalid_target_display_name` on refusal, replace exactly the root `saveDisplayName` property, then reparse the output and require the requested display name and player topology before returning it. Update the manifest from the reparsed payload, not from unchecked input.

- [ ] **Step 4: Run focused/full tests and commit**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter "FullyQualifiedName~PrepareForHost"`

Run: `dotnet test GameSaveHub.slnx --no-restore --nologo`

```powershell
git add src/GameSaveHub.Contracts/DiagnosticsContracts.cs src/GameSaveHub.Adapters.Abstractions/IGameSaveAdapter.cs src/GameSaveHub.Adapters.PlanetCrafter.GamePass/PlanetCrafterWorldTransformer.cs src/GameSaveHub.Adapters.PlanetCrafter.GamePass/PlanetCrafterGamePassAdapter.cs tests/Unit
git commit -m "feat: prepare permanent slot display name"
```

### Task 4: Add a dedicated permanent-slot replacement primitive

**Files:**
- Modify: `src/GameSaveHub.Contracts/DiagnosticsContracts.cs`
- Modify: `src/GameSaveHub.Adapters.Abstractions/IGameSaveAdapter.cs`
- Modify: `src/GameSaveHub.Adapters.PlanetCrafter.GamePass/PlanetCrafterGamePassAdapter.cs`
- Modify: `tests/Unit/PlanetCrafterGamePassAdapterTests.cs`

**Interfaces:**
- Produces:

```csharp
Task<ManagedSlotBaselineResult> CreateManagedSlotBaselineAsync(
    ManagedSlotReference slot, string outputRoot, CancellationToken cancellationToken = default);
Task<PortableImportResult> ReplaceManagedSlotAsync(
    PortableSaveArtifact artifact, string baselineDirectory, ManagedSlotReference slot,
    string expectedPlayerName, string preImportBackupOutputRoot,
    CancellationToken cancellationToken = default);
Task<ManagedSlotReconciliationResult> ReconcileManagedSlotReplacementAsync(
    PortableSaveArtifact artifact, string baselineDirectory, ManagedSlotReference slot,
    string expectedPlayerName, CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write failing baseline tests**

Cover: target selected only by logical name; target absent; display mismatch; player missing/ambiguous; game active; WGS mutation; baseline path overlapping WGS; all non-target worlds recorded as protected.

```csharp
[Fact]
public async Task ManagedBaselineAllowsOneDeclaredTargetAndProtectsEveryOtherWorld()
{
    var result = await adapter.CreateManagedSlotBaselineAsync(
        new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"), output);
    Assert.True(result.Success);
    Assert.Equal("Standard-5.json", result.Manifest!.Target.LogicalName);
    Assert.DoesNotContain(result.Manifest.ProtectedWorlds, x => x.LogicalName == "Standard-5.json");
}
```

- [ ] **Step 2: Run baseline tests and verify RED**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter "FullyQualifiedName~ManagedSlot"`

- [ ] **Step 3: Implement `managed-slot-baseline.json` schema 1**

The manifest contains adapter/package, timestamp, full file list, target logical/current/desired display names, target seed and before-payload hash, plus every other world as `ImportProtectedWorld`. Copy with per-file hashes, observe WGS again, and publish the baseline directory only after equivalence.

- [ ] **Step 4: Write failing replacement and rollback tests**

Cover nominal replacement, target changed since baseline, protected world changed, wrong prepared display name, topology invalid, mutation immediately before write, mutation after write, rollback success, rollback failure reported, identical artifact idempotence and no new logical world.

```csharp
[Fact]
public async Task ReplaceManagedSlotKeepsLogicalNameAndCreatesNoWorld()
{
    var before = await adapter.InspectLocalStorageAsync();
    var result = await adapter.ReplaceManagedSlotAsync(artifact, baseline, reference, "Stevenpwlk", backups);
    var after = await adapter.InspectLocalStorageAsync();
    Assert.True(result.Success);
    Assert.Equal(before.Worlds.Select(x => x.LogicalName), after.Worlds.Select(x => x.LogicalName));
    Assert.Equal("GSH-MONDE-PARTAGE", after.Worlds.Single(x => x.LogicalName == "Standard-5.json").DisplayName);
}
```

- [ ] **Step 5: Implement replacement without weakening placeholder checks**

Reuse the existing generation-safe WGS write internals, but validate against `ManagedSlotBaselineManifest`. Immediately before writing require: target hash equals baseline, all protected worlds equal baseline, game closed and WGS stable. After writing require: target logical name unchanged, payload hash equals prepared artifact, display name equals desired name, local player is the unique host `0`, protected worlds unchanged. On any post-write failure restore the full pre-import snapshot and return both primary and rollback errors.

- [ ] **Step 6: Write/implement reconciliation tests**

States are `PreviousPayloadPresent`, `ImportedPayloadPresent`, `TargetMissing`, `ProtectedWorldChanged`, `UnexpectedTargetContent`, `InvalidBaseline`, `InvalidArtifact`. Reconciliation is read-only and accepts only the before hash or expected imported hash.

- [ ] **Step 7: Verify placeholder regressions and commit**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter "FullyQualifiedName~PlanetCrafterGamePassAdapterTests"`

Run: `dotnet test GameSaveHub.slnx --no-restore --nologo`

```powershell
git add src/GameSaveHub.Contracts/DiagnosticsContracts.cs src/GameSaveHub.Adapters.Abstractions/IGameSaveAdapter.cs src/GameSaveHub.Adapters.PlanetCrafter.GamePass/PlanetCrafterGamePassAdapter.cs tests/Unit/PlanetCrafterGamePassAdapterTests.cs
git commit -m "feat: replace permanent WGS slot safely"
```

### Task 5: Make transfer sessions choose initial setup or reuse

**Files:**
- Modify: `src/GameSaveHub.Client.Orchestration/TransferModels.cs`
- Modify: `src/GameSaveHub.Client.Orchestration/TransferOrchestrator.cs`
- Create: `tests/Unit/Fixtures/transfer-session-v1.json`
- Modify: `tests/Unit/TransferOrchestratorTests.cs`

**Interfaces:**
- Adds: `TransferFlowKind.InitialSlotSetup`, `TransferFlowKind.ManagedSlotReuse`, `TransferFlowKind.LegacyPlaceholder`.
- Consumes: `IManagedSlotStore`, permanent adapter primitives.
- Produces: additive checkpoint fields; old session JSON remains readable.

- [ ] **Step 1: Write compatibility and flow tests**

```csharp
[Fact]
public async Task OldSessionJsonStillDeserializesAsLegacyPlaceholder()
{
    var json = await File.ReadAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "transfer-session-v1.json"));

    var session = JsonSerializer.Deserialize<TransferSession>(json, JsonOptions);

    Assert.NotNull(session);
    Assert.Equal(TransferFlowKind.LegacyPlaceholder, session.FlowKind);
    Assert.Null(session.ManagedSlotLogicalName);
}

[Fact]
public async Task ReuseImportsImmediatelyWithoutAwaitingPlaceholder()
{
    await managedSlotStore.WriteAsync(Binding("Standard-5.json"));
    var result = await orchestrator.StartAsync(WorldId, "Stevenpwlk", TransferFlowKind.ManagedSlotReuse);
    Assert.Equal(TransferStage.ReadyToPlay, result.Session!.Stage);
    Assert.Null(result.Session.PlaceholderName);
    Assert.Equal("Standard-5.json", result.Session.TargetLogicalName);
}
```

Also test first setup reaches `AwaitingPlaceholder` with exactly `GSH-MONDE-PARTAGE`, binding is absent before import, binding is written after validated import, crash after import before binding is reconciled, and two reuses keep the same logical name.

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter FullyQualifiedName~TransferOrchestratorTests`

- [ ] **Step 3: Add additive checkpoints**

Append optional constructor parameters so old JSON remains readable and old fields retain their order:

```csharp
TransferFlowKind FlowKind = TransferFlowKind.LegacyPlaceholder,
string? ManagedSlotCurrentDisplayName = null,
string? ManagedSlotDesiredDisplayName = null,
bool ManagedSlotBindingCommitted = false
```

Do not bump the session schema solely for these optional fields.

- [ ] **Step 4: Implement both branches**

Initial setup uses the existing new-world baseline/probe path with fixed placeholder `GSH-MONDE-PARTAGE`. Reuse calls `CreateManagedSlotBaselineAsync` and proceeds directly to `Importing`. Both call `PrepareForHostAsync(downloadedArtifact, session.PlayerName, ManagedSlotConstants.DisplayName, preparedOutputRoot, cancellationToken)`.

After a validated initial or legacy migration import, persist session state, write `managed-slot.json`, then persist `ManagedSlotBindingCommitted=true`. Recovery first reconciles WGS; if the imported payload is present and the binding commit is missing, it writes the binding exactly once. Daily replacement never changes the binding logical name.

- [ ] **Step 5: Verify capture still exports only the session logical name**

Add a regression where a visible homonym exists and assert `ExportPortableArtifactByLogicalNameAsync("Standard-5.json")` is the only capture call.

- [ ] **Step 6: Run full tests and commit**

Run: `dotnet test GameSaveHub.slnx --no-restore --nologo`

```powershell
git add src/GameSaveHub.Client.Orchestration/TransferModels.cs src/GameSaveHub.Client.Orchestration/TransferOrchestrator.cs tests/Unit/TransferOrchestratorTests.cs
git commit -m "feat: reuse permanent slot in transfers"
```

### Task 6: Bind existing and legacy slots explicitly in the service

**Files:**
- Create: `src/GameSaveHub.Client.Service/ManagedSlotCoordinator.cs`
- Create: `tests/Unit/ManagedSlotCoordinatorTests.cs`
- Modify: `src/GameSaveHub.Client.Service/ClientServiceOptions.cs`
- Modify: `src/GameSaveHub.Client.Service/Program.cs`
- Modify: `src/GameSaveHub.Client.Service/PipeServerWorker.cs`
- Modify: `src/GameSaveHub.Client.Orchestration/TransferTransitionGate.cs`

**Interfaces:**
- Produces pipe commands: `managed-slot-bind-existing`, `maintenance-status`.
- Produces `ManagedSlotHomeStatus` inside `home-context`.

- [ ] **Step 1: Write failing coordinator tests**

Test successful binding of the sole `Standard-5.json / GSH-SHLAGS-RETURN`, refusal for two legacy candidates, refusal while game runs, refusal while a transfer exists, reinspection immediately before store write, and no WGS mutation.

```csharp
[Fact]
public async Task BindLegacyCandidateReinspectsAndWritesOnlyBinding()
{
    var result = await coordinator.BindExistingAsync("Stevenpwlk");
    Assert.True(result.Success);
    Assert.Equal("Standard-5.json", (await store.ReadAsync())!.LogicalName);
    Assert.Equal(0, adapter.WriteCount);
}
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter FullyQualifiedName~ManagedSlotCoordinatorTests`

- [ ] **Step 3: Implement coordinator and shared gate state**

`TransferTransitionGate` exposes `bool IsBusy` backed by an atomic counter. Every bind/setup/repair command runs inside the same gate. Add `ManagedSlotStatePath = "%ProgramData%\\GameSaveHub\\managed-slot.json"` and inject store/coordinator. Add `bool IsWriteInProgress` to `ITransferSessionStore`; `FileTransferSessionStore` increments it before entering a durable write and decrements it only after `session.json` and `events.ndjson` have both been flushed to disk. Test both the in-flight and post-flush values. Memory test stores expose the same property deterministically.

- [ ] **Step 4: Extend home context and dispatch**

`home-context` reads binding, WGS inspection and resolution only when safe. It returns a presentation-safe `ManagedSlotHomeStatus(Status, DisplayName, RequiresExplicitBinding)` without logical name. `transfer-start` selects setup/reuse from the fresh resolution under the gate; it refuses ambiguous/repair states. `managed-slot-bind-existing` takes no logical name from the UI and recomputes the candidate server-side.

`maintenance-status` returns:

```csharp
new MaintenanceSafetyStatus(
    GameClosed: !process.IsRunning,
    NoActiveTransfer: active.Count == 0,
    TransitionIdle: !transitionGate.IsBusy,
    CheckpointDurable: !sessionStore.IsWriteInProgress,
    SafeToUpdate: allConditions);
```

It is read-only and never repairs or migrates WGS.

- [ ] **Step 5: Add pipe/home tests and commit**

Extend `HomeContextConsistencyTests` and service-facing tests for missing, ready, legacy, ambiguous and busy states.

Run: `dotnet test GameSaveHub.slnx --no-restore --nologo`

```powershell
git add src/GameSaveHub.Client.Service src/GameSaveHub.Client.Orchestration/TransferTransitionGate.cs tests/Unit/ManagedSlotCoordinatorTests.cs tests/Unit/HomeContextConsistencyTests.cs
git commit -m "feat: coordinate managed slot from service"
```

### Task 7: Present configuration, binding and daily reuse clearly

**Files:**
- Modify: `src/GameSaveHub.Client.Orchestration/HomeContextModels.cs`
- Modify: `src/GameSaveHub.Client.Orchestration/HomeStatePresenter.cs`
- Modify: `src/GameSaveHub.Client.Orchestration/HomeActionErrorPresenter.cs`
- Modify: `tests/Unit/HomeStatePresenterTests.cs`
- Modify: `tests/Unit/HomeActionErrorPresenterTests.cs`
- Modify: `src/GameSaveHub.Client.App/MainWindow.xaml`
- Modify: `src/GameSaveHub.Client.App/MainWindow.xaml.cs`

**Interfaces:**
- Adds actions: `ConfigureManagedSlot`, `BindExistingManagedSlot`.
- Adds view fields: `SlotName`, `ShowCopySlotName`, `CopyConfirmationText`.

- [ ] **Step 1: Write failing presenter tests for the full state table**

```csharp
[Fact]
public void MissingSlotAndFreeWorldOffersOneTimeConfiguration()
{
    var view = HomeStatePresenter.Present(Context(slot: ManagedSlotStatus.Missing));
    Assert.Equal("Configurons ce PC", view.Title);
    Assert.Equal(HomePrimaryAction.ConfigureManagedSlot, view.PrimaryAction);
}

[Fact]
public void ReadySlotAndFreeWorldKeepsDailyTakeControlAction()
{
    var view = HomeStatePresenter.Present(Context(slot: ManagedSlotStatus.Ready));
    Assert.Equal("Le monde est prêt", view.Title);
    Assert.Equal(HomePrimaryAction.StartTransfer, view.PrimaryAction);
    Assert.Equal("Prendre la main", view.PrimaryActionLabel);
}

[Fact]
public void RemoteHostTakesPriorityOverLocalSlotSetup()
{
    var view = HomeStatePresenter.Present(Context(
        slot: ManagedSlotStatus.Missing,
        remoteSessionState: "InGame",
        remotePlayerName: "Bob"));
    Assert.Equal(HomeVisualState.RemoteHosting, view.State);
    Assert.Equal(HomePrimaryAction.LaunchGame, view.PrimaryAction);
    Assert.Equal("Lancer The Planet Crafter", view.PrimaryActionLabel);
}
```

Cover initial steps 1/2 and 2/2, legacy bind, repair stop, active game outside Hub, remote host, update required and copy text exactly `GSH-MONDE-PARTAGE`.

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter "FullyQualifiedName~HomeStatePresenterTests|FullyQualifiedName~HomeActionErrorPresenterTests"`

- [ ] **Step 3: Implement pure presentation**

Order precedence: unenrolled → server unavailable → safety inconsistency → local active session → remote session → game outside Hub → slot status → ready. This lets an unconfigured player launch the game to join Bob without configuring a host slot.

- [ ] **Step 4: Add native WPF controls**

Add a collapsed `SlotNamePanel` containing a read-only `TextBox` and `Copier le nom` button. On click call `Clipboard.SetText(_view.SlotName)` and show `Copié` for two seconds using a cancellable dispatcher timer. Keep the copied text selectable, preserve keyboard focus, set `AutomationProperties.Name`, and never use a rasterized text asset.

`ConfigureManagedSlot` sends `transfer-start`; `BindExistingManagedSlot` sends `managed-slot-bind-existing`. WPF remains a thin adapter and contains no slot selection logic.

- [ ] **Step 5: Build WPF and run tests**

Run: `dotnet build src/GameSaveHub.Client.App/GameSaveHub.Client.App.csproj --no-restore --nologo`

Run: `dotnet test GameSaveHub.slnx --no-restore --nologo`

- [ ] **Step 6: Commit**

```powershell
git add src/GameSaveHub.Client.Orchestration/HomeContextModels.cs src/GameSaveHub.Client.Orchestration/HomeStatePresenter.cs src/GameSaveHub.Client.Orchestration/HomeActionErrorPresenter.cs src/GameSaveHub.Client.App tests/Unit/HomeStatePresenterTests.cs tests/Unit/HomeActionErrorPresenterTests.cs
git commit -m "feat: guide permanent slot setup"
```

### Task 8: Add the minimum-client acquisition fence

**Files:**
- Create: `src/GameSaveHub.Contracts/ClientCompatibilityPolicy.cs`
- Create: `tests/Unit/ClientCompatibilityPolicyTests.cs`
- Modify: `src/GameSaveHub.Client.Service/AuthenticatedTransferServerClient.cs`
- Modify: `src/GameSaveHub.Server.Api/ApiOptions.cs`
- Modify: `src/GameSaveHub.Server.Api/Program.cs`
- Modify: `src/GameSaveHub.Server.Api/appsettings.json`
- Modify: API contract tests.

**Interfaces:**
- Client header: `X-GameSaveHub-Client-Version: 0.4.0`.
- Server option: `ClientCompatibility:MinimumAcquireVersion` nullable.
- Failure: HTTP 409, code `client_update_required`.

- [ ] **Step 1: Write failing version policy tests**

```csharp
[Theory]
[InlineData(null, "0.4.0", false)]
[InlineData("0.3.9", "0.4.0", false)]
[InlineData("0.4.0", "0.4.0", true)]
[InlineData("0.4.1", "0.4.0", true)]
public void AcquireCompatibilityIsDeterministic(string? client, string minimum, bool allowed)
    => Assert.Equal(allowed, ClientCompatibilityPolicy.CanAcquire(client, minimum));
```

Also cover malformed versions and an empty minimum, which must remain non-constraining during additive rollout.

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test tests/Unit/GameSaveHub.UnitTests.csproj --filter FullyQualifiedName~ClientCompatibilityPolicyTests`

- [ ] **Step 3: Implement policy, header and API guard**

Use three-component numeric versions only. Add the header to every authenticated request. At the acquire endpoint, evaluate the header before creating any session; return `client_update_required` without database mutation. Leave the deployed/default minimum empty until both pilot clients are compatible.

- [ ] **Step 4: Verify old JSON and old clients can still read status**

Add contract tests proving status/list endpoints are unaffected and missing header is rejected only when a minimum is configured.

- [ ] **Step 5: Run full tests and commit**

Run: `dotnet test GameSaveHub.slnx --no-restore --nologo`

```powershell
git add src/GameSaveHub.Contracts/ClientCompatibilityPolicy.cs src/GameSaveHub.Client.Service/AuthenticatedTransferServerClient.cs src/GameSaveHub.Server.Api tests/Unit/ClientCompatibilityPolicyTests.cs tests/Unit/ApiContractCompatibilityTests.cs
git commit -m "feat: require compatible client for acquisition"
```

### Task 9: Package a slot-aware manual baseline for Lot 3

**Files:**
- Modify: `src/GameSaveHub.Client.Service/ClientServiceOptions.cs`
- Modify: `src/GameSaveHub.Client.Service/appsettings.json`
- Modify: `tools/INSTALL-GAMESAVEHUB-CLIENT.ps1`
- Modify: `tools/STATUS-GAMESAVEHUB-CLIENT.ps1`
- Modify: `tools/build-integrated-phase3.ps1`
- Modify: `SOURCE-SHA256SUMS.txt`

**Interfaces:**
- Package version: `0.4.0-pilot`.
- Persistent path: `%ProgramData%\GameSaveHub\managed-slot.json`.

- [ ] **Step 1: Add failing package assertions**

Extend the build script’s self-checks to require the new state path, client version header, `maintenance-status`, fixed slot name, app/service executables and absence of updater binaries. It must fail against the pre-change package.

- [ ] **Step 2: Run and verify the packaging check fails for the expected missing markers**

Run: `powershell -ExecutionPolicy Bypass -File tools/build-integrated-phase3.ps1`

- [ ] **Step 3: Update installer/build behavior**

The installer continues to preserve the whole ProgramData directory and CNG key. It writes `ManagedSlotStatePath` into local settings, stops the service cleanly before file copy and prints whether a binding already exists without displaying its logical name. Produce `GameSaveHub-Client-Lot2-0.4.0-PILOTE-win-x64.zip` plus SHA-256.

- [ ] **Step 4: Regenerate and verify `SOURCE-SHA256SUMS.txt`**

Hash every tracked source included by the existing manifest policy, compare immediately, and ensure no build artifact path enters Git.

- [ ] **Step 5: Build package and commit**

Run: `powershell -ExecutionPolicy Bypass -File tools/build-integrated-phase3.ps1`

Run: `Get-FileHash -Algorithm SHA256 artifacts\GameSaveHub-Client-Lot2-0.4.0-PILOTE-win-x64.zip`

```powershell
git add src/GameSaveHub.Client.Service tools/INSTALL-GAMESAVEHUB-CLIENT.ps1 tools/STATUS-GAMESAVEHUB-CLIENT.ps1 tools/build-integrated-phase3.ps1 SOURCE-SHA256SUMS.txt
git commit -m "build: package slot-aware pilot client"
```

### Task 10: Update operational documentation and the live checklist

**Files:**
- Modify: `docs/operations/LOT2-CONTEXTUAL-CLIENT.md`
- Modify: `docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`
- Create: `docs/operations/PERMANENT-SLOT-PILOT.md`

**Interfaces:**
- Produces the operator procedure used in Tasks 12–14.

- [ ] **Step 1: Document the exact daily and first-use flows**

Include fixed name, explicit legacy binding, snapshot locations, refusal codes, `maintenance-status`, no automatic deletion and the rule that a daily transfer must not increase the set of logical WGS names.

- [ ] **Step 2: Replace stale checklist counts with evidence-backed status**

The checklist has four sections: `Validé`, `Implémenté mais à valider réellement`, `À implémenter`, `Portes externes`. Every line has a date/evidence or an unchecked box. Keep the 202-test baseline and increment it only from actual output.

- [ ] **Step 3: Add the pilot runbook**

The runbook contains preflight, snapshot, explicit UAC notice, binding `Standard-5.json` without showing it in the daily UI, first reuse, second reuse, restart, interruption, server checks, rollback and cleanup. Every WGS/NAS write has a separate approval gate.

- [ ] **Step 4: Review docs and commit**

Run: `$patterns = @(('6' + '3 cas'), ('launch' + [char]61 + 'false')); rg -n ($patterns -join '|') docs/operations`

Expected: no stale claim in the three touched documents.

```powershell
git add docs/operations/LOT2-CONTEXTUAL-CLIENT.md docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md docs/operations/PERMANENT-SLOT-PILOT.md
git commit -m "docs: define permanent slot operations"
```

### Task 11: Complete automated verification and review gates

**Files:**
- Modify only files required by failures found in the commands below, always with a failing regression test first.

- [ ] **Step 1: Run pristine restore/build/test**

Run:

```powershell
dotnet restore GameSaveHub.slnx --nologo
dotnet build GameSaveHub.slnx --no-restore --nologo
dotnet test GameSaveHub.slnx --no-build --nologo
```

Expected: 0 warnings, 0 errors, at least 202 tests, 0 failures.

- [ ] **Step 2: Run targeted safety suites**

Run filters for `ManagedSlot`, `PlanetCrafterGamePassAdapterTests`, `TransferOrchestratorTests`, `GameLifecycleMonitorTests`, `HomeStatePresenterTests`, `ClientCompatibilityPolicyTests`, `TransferTransitionGateTests` and `ApiContractCompatibilityTests`.

- [ ] **Step 3: Verify repository/package integrity**

Run `git diff --check`, verify `SOURCE-SHA256SUMS.txt`, rebuild the ZIP, compare its emitted hash, and scan the ZIP for path traversal and unexpected files.

- [ ] **Step 4: Conduct code review**

Review specifically: any selection by display name, any path that skips the shared transition gate, any WGS write without snapshot, any old-session JSON incompatibility, any update-safe result while slot work is active, and any exception/path/hash leaked to the home UI.

- [ ] **Step 5: Commit regression fixes separately**

Use one commit per independently reviewable issue, with the failing test and fix together.

### Task 12: Install and migrate Steven’s pilot safely

**Files affected outside Git:**
- `%ProgramFiles%\GameSaveHub\Client`
- `%ProgramData%\GameSaveHub`
- Steven’s WGS only after explicit approval.

- [ ] **Step 1: Reconfirm idle state and create backups**

Require server healthy, world `Available`, no local/server session, game closed, WGS stable. Create and validate a full WGS snapshot plus a ProgramData backup. Record file counts and manifest hashes.

- [ ] **Step 2: Obtain explicit installation approval and install `0.4.0-pilot`**

Warn before UAC. Install manually because this release is the future Lot 3 rollback baseline. Verify service `Running/Automatic`, app/service hashes, existing device/pseudo/CNG preservation and pipe response under 30 seconds.

- [ ] **Step 3: Bind the sole legacy candidate explicitly**

The app must show a one-time binding action for the unique remaining `GSH-SHLAGS-RETURN`. After the user confirms, verify `managed-slot.json` points to the existing logical world, WGS bytes did not change and the home returns to `Prendre la main`.

- [ ] **Step 4: Obtain explicit WGS approval and run first reuse**

Take control. Assert no new logical world appears; the existing slot becomes `GSH-MONDE-PARTAGE`. Launch Xbox, load that exact visible world, make a harmless identifiable change, save and close. Verify capture/publication, version increment, player/source identity, server availability and WGS stable.

- [ ] **Step 5: Run second reuse of the same logical slot**

Repeat acquisition and play. Compare inventories before/after and prove the set of logical WGS names is unchanged and the bound logical name is identical across both sessions.

- [ ] **Step 6: Record evidence and update checklist**

Store session/event summaries and hashes without secrets; check off only observed items.

### Task 13: Validate resilience, context and accessibility

**Files affected outside Git:** local service/app and WGS during controlled tests.

- [ ] **Step 1: Validate service restart and updater health primitive**

At idle, restart the service, confirm the app shows temporary unavailability, then recovers automatically. Call `maintenance-status` and require `SafeToUpdate=true` within 30 seconds.

- [ ] **Step 2: Validate restart during `InGame`**

With approval, reboot Windows while a recoverable session exists. After login, verify the same session checkpoint, no re-import, game-closed capture and successful publication.

- [ ] **Step 3: Validate interruption during managed-slot import**

Use the existing controlled interruption mechanism at the documented checkpoint. On restart, reconciliation must identify either previous or imported payload, retry only with explicit permission when previous remains, and never create a second slot.

- [ ] **Step 4: Validate contextual states**

Verify: game launched outside Hub, server unavailable/recovered, remote host preparing, remote host playing, join launch, client update required and manual-review slot mismatch. Use a controlled test API/session where Bob is unavailable; repeat remote host for real in Task 14.

- [ ] **Step 5: Validate UI quality**

At 1440×1024 and Windows scaling 100%, 125%, 150%, 200%, inspect no clipping/overflow, keyboard-only flow, visible focus, screen-reader names/live regions, copy feedback, cancellation of refresh and absence of technical text. Compare the final home screenshot with the accepted Lot 2 visual reference.

- [ ] **Step 6: Update checklist and commit documentation evidence**

Do not commit screenshots containing personal data or tokens. Commit only sanitized reports and checklist changes.

### Task 14: Validate Bob and close Lot 2 before Lot 3

**External gate:** Bob must be available; do not mark Lot 2 complete without this task.

- [ ] **Step 1: Install the exact verified `0.4.0-pilot` package on Bob**

Use the same ZIP hash as Steven. Verify enrollment, service health and no existing managed-slot binding.

- [ ] **Step 2: Run Bob’s one-time slot configuration**

Create `GSH-MONDE-PARTAGE` once, use `Copier le nom`, close, import and launch. Verify exactly one managed world and a durable binding.

- [ ] **Step 3: Run `Steven → Bob → Steven`**

Each host loads `GSH-MONDE-PARTAGE`, makes an identifiable change, saves and closes. At every handoff verify player topology, inventory, equipment, position, published-by player, version IDs and absence of new logical worlds.

- [ ] **Step 4: Validate remote-host joining**

While Bob hosts, Steven’s app must show Bob, offer Xbox launch without acquisition, and allow the normal invitation-code join flow. Repeat symmetrically.

- [ ] **Step 5: Promote the slot-aware baseline for Lot 3**

Only after both PCs pass, configure `ClientCompatibility.MinimumAcquireVersion=0.4.0` with explicit NAS approval. Verify old/no-version acquisition gets `client_update_required`, status remains readable, then record `0.4.0` as the oldest automatic rollback target for Lot 3.

- [ ] **Step 6: Final closure review**

Run all automated verification again, review NAS/API/client logs, confirm no active session, create final backups, update the checklist to `Lot 2 validé`, and request user approval before merging to `main`.

---

## Definition of Done

The implementation is complete only when all of the following are true:

- all plan checkboxes through Task 14 are checked with evidence;
- automated tests are green with a total not lower than 202;
- Steven has reused the same logical slot twice without any new `Standard-X`;
- Bob has completed first setup and the real `Steven → Bob → Steven` cycle;
- remote hosting/joining, restart, import interruption and updater health status are proven;
- `managed-slot.json` survives reinstall and contains no secret;
- the minimum client fence is enabled only after both pilot clients are compatible;
- the application exposes one clear daily action and no technical identifier;
- WGS, server database and release artifacts have verified backups;
- the checklist and runbook match the observed state;
- the user has approved the final result and the merge to `main`.
