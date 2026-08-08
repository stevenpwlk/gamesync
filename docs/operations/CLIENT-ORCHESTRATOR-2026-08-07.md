# GameSave Hub — Client Orchestrator consolidation — 2026-08-07

## Statut

Cette phase transforme le mécanisme Planet Crafter validé en flux client/service persistant.

Le feature gate serveur `AllowHostTransfer` reste **fermé**. Le code peut être compilé et testé sans rendre le transfert disponible en production.

## Invariants de sécurité

1. Le pseudo demandé doit déjà exister exactement une fois dans l'artefact source.
2. L'artefact préparé doit avoir ce joueur en ID 0 et unique hôte avant toute écriture WGS.
3. Tous les mondes présents avant le placeholder sont capturés dans une baseline et protégés par hash.
4. La cible doit être l'unique nouveau `Standard-X`, d'index supérieur à la baseline.
5. Le placeholder est sondé en lecture seule avant `import-starting` et son hash est persisté.
6. Une écriture WGS n'est jamais répétée automatiquement après un crash.
7. Si un redémarrage survient pendant l'import :
   - hash artefact présent → import considéré terminé sans nouvelle écriture ;
   - hash placeholder présent → session `Interrupted`, reprise explicite requise ;
   - autre hash / monde protégé modifié → `ManualReview`.
8. Après `import-starting`, l'abandon local est interdit : la session serveur reste verrouillée jusqu'à publication réussie ou intervention d'administration.
9. La capture finale n'est faite qu'après fermeture du jeu et stabilité WGS.
10. Les uploads sont repris par hash/chunks confirmés ; le commit est idempotent.
11. Plusieurs sessions locales actives simultanément déclenchent `ManualReview` et aucune reprise automatique.
12. Le named pipe reste limité au SID Windows enregistré et à LocalSystem.
13. Le service LocalSystem ne doit jamais utiliser son propre `%LOCALAPPDATA%` : le profil du joueur est résolu à partir de `RegisteredUserSid` via `HKLM\...\ProfileList`, puis injecté dans l'adapter.
14. Un heartbeat serveur est envoyé toutes les 30 secondes pendant toute session active afin de rester sous le watchdog serveur de 90 secondes.

## Machine d'état locale

`Initialized`
→ `Acquiring`
→ `DownloadingArtifact`
→ `PreparingArtifact`
→ `CreatingBaseline`
→ `AwaitingPlaceholder`
→ `Importing`
→ `ReadyToPlay`
→ `InGame`
→ `CapturingResult`
→ `UploadPending`
→ `Uploading`
→ `Publishing`
→ `Completed`

États de sûreté :

- `Interrupted` : reprise possible à partir d'un checkpoint connu ;
- `ManualReview` : aucune nouvelle écriture automatique ;
- `Aborted` : uniquement avant `import-starting` ;
- `Failed` : échec avant import, verrou serveur libéré si nécessaire.

## Persistance

Chaque session locale possède :

`%ProgramData%\GameSaveHub\transfers\<local-session-guid>\`

avec :

- `session.json` : checkpoint atomique courant ;
- `events.ndjson` : journal d'audit append-only ;
- `inbound\` : artefact serveur téléchargé ;
- `prepared\` : artefact préparé pour l'hôte local ;
- `safety\import-baselines\` : baseline WGS ;
- `safety\pre-import\` : snapshot juste avant écriture ;
- `outbound\` : artefact resauvegardé à publier.

`session.json` est écrit via fichier temporaire + flush disque + move atomique.

## Reprise après interruption

### Acquisition serveur

La clé d'idempotence d'acquisition est créée et persistée **avant** l'appel réseau. Si le service s'arrête après que le serveur a créé le verrou mais avant le checkpoint local, le même appel peut être rejoué avec la même clé et récupère la même session serveur.

### Import WGS

Le checkpoint `Importing` contient avant écriture :

- session serveur ;
- baseline ;
- artefact préparé ;
- `TargetLogicalName` ;
- hash exact du placeholder ;
- pseudo attendu.

Au redémarrage, `ReconcilePortableImportAsync` est strictement en lecture seule.

### Upload

Le client redemande toujours le manifeste d'upload avec le même hash/longueur. Le serveur renvoie l'upload existant et les chunks déjà reçus. Seuls les chunks manquants sont envoyés.

Le serveur remet aussi une session `Interrupted` en `UploadPending` lorsqu'un upload existant est repris. Un commit déjà passé renvoie la même `VersionId`; un commit interrompu en état `Publishing` peut être rejoué.

Le checkpoint local `Publishing` signifie que tous les chunks ont déjà été confirmés et que l'`UploadId` est connu. Au redémarrage, le client **rejoue directement ce commit connu avant tout appel `CreateUpload`**. Cela couvre le cas critique où le serveur a déjà terminé et libéré la session, mais où la réponse HTTP du commit a été perdue avant l'écriture de `session.json`. Aucun second upload n'est créé.

## IHM pilote

L'application WPF contient un panneau « Orchestrateur pilote » :

- ID du monde serveur ;
- pseudo déjà présent dans la sauvegarde ;
- démarrer ;
- confirmer le placeholder ;
- confirmer que le jeu est lancé ;
- confirmer sauvegarde + fermeture ;
- reprendre ;
- abandonner avant import.

Le lancement automatique de Planet Crafter reste désactivé (`CanLaunchGame=false`).

## Gate de production

Doivent rester faux pendant cette phase :

- `src/GameSaveHub.Server.Api/appsettings.json` → `FeatureGates:AllowHostTransfer=false`
- `deploy/compose.portainer.yml` → `FeatureGates__AllowHostTransfer: "false"`
- `deploy/compose.yml` → `${GSH_ALLOW_HOST_TRANSFER:-false}`

La prochaine décision d'ouverture devra être explicite et précédée d'un build/test réussi de cette phase.
