# 02 — Architecture et code

> Toutes les références de code de ce document portent sur la version la plus récente, qui se trouve **dans l'archive** `GameSaveHub-Integrated-Client-Phase3-0.3.0-r2-source.zip` et non dans `src/` sur disque. Voir [04 — État du dépôt](04-ETAT-DU-DEPOT.md).

## Stack

| Élément | Choix |
|---|---|
| Plateforme | .NET 10 LTS (`global.json` : SDK `10.0.302`, `rollForward: latestPatch`) |
| Discipline de compilation | `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended` |
| Serveur | ASP.NET Core Minimal API, EF Core, SQLite en mode WAL |
| Client | Service Windows (LocalSystem) + application WPF, dialogue par named pipe |
| Conteneurs | Alpine, publication `linux-musl-x64`, non-root `uid=100 gid=100` |
| Reverse proxy | Traefik, TLS ACME DNS-01 OVH |

Le choix de .NET 10 remonte à une correction faite dès la conception : .NET 8 arrivait en fin de support le 10 novembre 2026, .NET 10 LTS est maintenu jusqu'en novembre 2028.

## Les 13 projets

```text
src/
  GameSaveHub.Contracts                        contrats API + DiagnosticsContracts + PlayerCompatibilityRules
  GameSaveHub.Core                             FileSafety, RetentionPolicy, WorldSessionState(+Machine)
  GameSaveHub.Adapters.Abstractions            IGameSaveAdapter
  GameSaveHub.Adapters.PlanetCrafter.GamePass  l'adapter WGS (~81 Ko) + PlanetCrafterWorldTransformer
  GameSaveHub.Diagnostics                      CLI d'investigation et pilote
  GameSaveHub.Client.Orchestration             TransferOrchestrator (~42 Ko), machine d'états persistante
  GameSaveHub.Client.Service                   service Windows : identité, pipe, heartbeat, reprise
  GameSaveHub.Client.App                       application WPF joueur
  GameSaveHub.Client.Probe                     WPF autonome de diagnostic (PC distant)
  GameSaveHub.Server.Api                       API HTTP (~29 Ko de Program.cs)
  GameSaveHub.Server.Infrastructure            EF Core, migrations, stockage immuable, validateur
  GameSaveHub.Server.Admin                     CLI d'administration (conteneur one-shot)
  GameSaveHub.DynHost                          updater DynHost OVH
tests/
  Unit                                         xUnit
  EndToEnd/GameSaveHub.ApiSmoke                scénario API bout en bout
```

`GameSaveHub.Client.Orchestration` et `PlanetCrafterWorldTransformer` sont **absents du `src/` sur disque** — ils n'existent que dans les ZIP.

## API serveur

Toutes les routes protégées exigent un JWT ; les mutations acceptent une clé d'idempotence.

**Publiques**

```text
GET  /healthz
POST /api/v1/enrollments/redeem
POST /api/v1/auth/challenges
POST /api/v1/auth/tokens
```

**Protégées**

```text
GET  /api/v1/worlds                        ← ajouté en 0.3.0
GET  /api/v1/worlds/{id}/preview           ← ajouté en 0.3.0
GET  /api/v1/worlds/{id}/status
POST /api/v1/worlds/{id}/acquire
POST /api/v1/sessions/{id}/import-starting
GET  /api/v1/sessions/{id}/artifact
POST /api/v1/sessions/{id}/heartbeat
POST /api/v1/sessions/{id}/uploads
PUT  /api/v1/uploads/{id}/chunks/{index}
POST /api/v1/uploads/{id}/commit
POST /api/v1/sessions/{id}/abort
POST /api/v1/sessions/{id}/report-failure
```

`GET /worlds/{id}/preview` ne renvoie **jamais** le payload de sauvegarde : uniquement nom du monde, version courante, hash d'artefact, nom affiché, seed, et la liste des joueurs (ID, pseudo, host, IDs inventaire/équipement). L'artefact immuable est validé et son payload rehashé avant de produire le preview.

### Authentification

Enrôlement par **code d'invitation à usage unique**, puis clé **ECDSA P-256 CNG** créée sur le PC (`Microsoft Software KSP`, `MachineKey`, usage signature, `CngExportPolicies.None`). La clé privée ne quitte jamais la machine. Challenge signé → jeton JWT de courte durée (HMAC-SHA256). Révocation par appareil.

`DeviceId` et pseudo local sont persistés dans `%ProgramData%\GameSaveHub\client-state.json`.

### Persistance

Entités EF Core : `WorldEntity`, `DeviceEntity`, `EnrollmentEntity`, `AuthChallengeEntity`, `SessionEntity`, `SaveVersionEntity`, `UploadEntity`, `UploadChunkEntity`, `IdempotencyEntity`, `AdminAuditEntity`.

Quatre migrations, toutes datées du 3 août — **aucune migration n'a été ajoutée depuis** (c'est ce qui rend le rollback `0.3.0 → 0.2.0` direct) :

```text
20260803070540_InitialCreate
20260803072739_AddAdminAuditAndInitialImports
20260803072903_StoreTimestampsAsIntegers
20260803073041_AddVersionProtection
```

### Machine d'états serveur

`GameSaveHub.Core.WorldSessionState` :

```text
Preparing → InGame → UploadPending → Publishing → Completed
```

Transitions de sûreté : `Interrupted` est atteignable depuis `Preparing`, `InGame`, `UploadPending` et `Publishing`, et permet de repartir vers `UploadPending`, `Publishing` ou `Failed`. `Aborted` n'est atteignable que depuis `Preparing` — donc **uniquement avant `import-starting`**. `Failed` est atteignable depuis tout état sauf `Completed` et `Aborted`.

Un watchdog serveur passe une session en `Interrupted` après 90 secondes sans heartbeat, **sans jamais libérer le verrou de monde**. Le client émet un heartbeat toutes les 30 secondes.

Un `PublicationReconciler` répare au démarrage les publications interrompues.

### Stockage immuable

`ImmutableArtifactStore` : upload dans `pending/`, validation, renommage atomique vers `objects/` adressé par hash, transaction SQLite du pointeur de version courante, puis libération du verrou. SQLite ne stocke que les métadonnées.

Limites en production : `Storage__MaxArtifactBytes = 67108864` (64 Mio), `Storage__MaxChunkBytes = 4194304` (4 Mio). Rétention configurée : 20 dernières versions, 30 quotidiennes, 26 hebdomadaires.

## Format `.gshsave`

Archive ZIP **non compressée** contenant exactement deux entrées :

```text
manifest.json
payload/world.save
```

Ne contient jamais `containers.index`, `container.*`, un GUID de blob WGS, `PlayerPrefs.json`, les succès, ni un chemin absolu du PC source.

`ArtifactEnvelopeValidator` refuse : toute entrée supplémentaire ou tout chemin différent des deux autorisés, un manifeste > 64 Kio, un monde vide ou > 256 Mio, un ratio de compression excessif, une taille ou un SHA-256 divergent du manifeste, un monde illisible, ou un nom logique / nom affiché / seed / tableau de joueurs différent du contenu réel.

> **Écart doc/code repéré :** `docs/investigation/ARTIFACT-FORMAT.md` annonce un refus au-delà d'un ratio de compression de **10**, alors que `ArtifactEnvelopeValidator.cs` utilise `MaximumCompressionRatio = 100`. À trancher : corriger la doc ou durcir le code.

## Orchestrateur client (Phase 2)

`GameSaveHub.Client.Orchestration` transforme le pilote en machine d'états persistante côté service Windows.

```text
Initialized → Acquiring → DownloadingArtifact → PreparingArtifact → CreatingBaseline
→ AwaitingPlaceholder → Importing → ReadyToPlay → InGame → CapturingResult
→ UploadPending → Uploading → Publishing → Completed
```

États de sûreté : `Interrupted` (reprise depuis un checkpoint connu), `ManualReview` (aucune écriture automatique), `Aborted` (uniquement avant `import-starting`), `Failed`.

Persistance par session locale, sous `%ProgramData%\GameSaveHub\transfers\<guid>\` :

```text
session.json                    checkpoint atomique (temp + flush + move)
events.ndjson                   journal d'audit append-only
inbound/                        artefact serveur téléchargé
prepared/                       artefact préparé pour l'hôte local
safety/import-baselines/        baseline WGS
safety/pre-import/              snapshot juste avant écriture
outbound/                       artefact resauvegardé à publier
```

Trois propriétés de reprise méritent d'être notées, parce qu'elles couvrent les cas réellement dangereux :

1. **La clé d'idempotence d'acquisition est persistée _avant_ l'appel réseau.** Si le service meurt après que le serveur a créé le verrou mais avant le checkpoint local, le même appel rejoué récupère la même session serveur.
2. **Un crash pendant l'écriture WGS ne déclenche aucune réécriture automatique.** `ReconcilePortableImportAsync` est strictement en lecture seule : hash artefact présent → import considéré terminé ; hash placeholder présent → `Interrupted`, reprise explicite requise ; autre hash ou monde protégé modifié → `ManualReview`.
3. **Un checkpoint `Publishing` rejoue directement le commit connu avant tout `CreateUpload`.** Cela couvre le cas où le serveur a terminé et libéré la session mais où la réponse HTTP a été perdue. Aucun second upload n'est créé.

### Résolution du profil Windows

Le service tourne en LocalSystem mais **ne doit jamais** utiliser son propre `%LOCALAPPDATA%`. `RegisteredUserProfileResolver` résout le profil du joueur depuis `ClientService:RegisteredUserSid` via `HKLM\...\ProfileList`, puis l'injecte dans l'adapter. Le fallback dangereux sur le SID du processus service **a été supprimé** — `RegisteredUserSid` est désormais réellement obligatoire.

Le named pipe est restreint au SID Windows enregistré et à LocalSystem.

## CLI Diagnostics

```text
inventory              inspection WGS strictement en lecture seule
capabilities           capacités déclarées par l'adapter
export-world           production d'un .gshsave
validate-artifact      validation d'enveloppe
snapshot               capture cohérente (jeu fermé + --acknowledge-test-world)
validate-snapshot      vérification d'une capture
compare                diff de deux manifestes de snapshot
restore-test-world     restauration ciblée hors ligne, monde jetable uniquement
prepare-host           échange d'IDs joueur pour placer le pseudo cible en ID 0
import-baseline        capture WGS complète avant création du placeholder
import-artifact        import ciblé (exige --acknowledge-pilot-import)
```

Capacités déclarées par l'adapter en Phase 3 : `canPrepareForHost=true`, `canImportPortableArtifact=true`, `canLaunchGame=false`, statut `pilot-validated-production-gate-required`.

## Tests

`tests/Unit` — 8 fichiers, 56 attributs `[Fact]`/`[Theory]`, **70 cas** après expansion des `[InlineData]`. Le build Phase 3 exige 70/70.

| Fichier | `[Fact]`/`[Theory]` |
|---|---|
| `PlanetCrafterGamePassAdapterTests.cs` | 24 |
| `TransferOrchestratorTests.cs` | 12 |
| `ArtifactEnvelopeValidatorTests.cs` | 4 |
| `PlayerCompatibilityRulesTests.cs` | 4 |
| `WorldSessionStateMachineTests.cs` | 4 |
| `FileSafetyTests.cs` | 3 |
| `ImmutableArtifactStoreTests.cs` | 3 |
| `RetentionPolicyTests.cs` | 2 |

`tests/EndToEnd/GameSaveHub.ApiSmoke` couvre un parcours API complet.

## Déploiement NAS

Stack Portainer `gamesavehub`, quatre services :

| Service | Image | Réseau | Exposition |
|---|---|---|---|
| `traefik` | `gamesavehub-traefik:0.1.0` | `edge` | `8443` + `18443` sur l'hôte |
| `api` | `gamesavehub-api:0.2.0` → cible `0.3.0` | `edge` + `backend` | interne `8080` |
| `dynhost` | `gamesavehub-dynhost:0.1.0` | `edge` | — |
| `admin` | `gamesavehub-admin:0.1.0` | `backend` | profil `tools`, one-shot |

`backend` est un réseau **interne**. Aucun socket Docker n'est monté dans Traefik (provider *file*). Aucune route d'administration n'est exposée sur `18443`.

Six secrets par fichier hôte, `mode 600`, `chown 100:100` :

```text
gsh_signing_key
ovh_application_key      ovh_application_secret      ovh_consumer_key
dynhost_username         dynhost_password
```

Les identifiants **DynHost** et les identifiants **API OVH pour DNS-01** sont strictement séparés, par conception.

Réseau :

```text
Internet  saves.stevenpwlk.fr:18443 → NAT Livebox TCP → NAS:8443 → Traefik
LAN       saves.stevenpwlk.fr:18443 → DNS local        → NAS:18443 → Traefik
```

Aucune règle sur le port `443` : les autres services du NAS restent intacts.

Volumes NAS : `/Volume2/gamesavehub/{data,secrets,letsencrypt-production,letsencrypt-staging,backups,imports}`.
