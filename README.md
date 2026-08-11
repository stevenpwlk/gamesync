# GameSave Hub

Partager en toute sûreté un monde multijoueur **The Planet Crafter** (PC Game Pass) entre plusieurs PC Windows 11 avec des comptes Xbox différents, chacun pouvant devenir hôte à son tour et retrouver le point exact où les autres se sont arrêtés.

**État au 11 août 2026 : `main` = Lot 1 fusionné (mini-exporteur + remplacement administratif du monde partagé, 153/153 tests). Le Lot 2 (accueil contextuel, lancement Xbox direct, slot local permanent `GSH-MONDE-PARTAGE`) est en cours sur la branche `codex/v1-lot2-contextual-app` et n'est pas encore fusionné. Le Lot 3 (installateur unique signé + mises à jour à distance) est en cours sur sa propre branche `codex/v1-lot3-setup-updater` — implémentation automatisée terminée, aucune validation réelle sur PC physique.**

> 📍 **Source de vérité sur l'avancement réel** (fait / en cours / porte externe à faire valider avec Bob) : [`docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`](docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md). Le dossier `docs/etat-des-lieux-2026-08-08/` est une archive figée au 8-9 août ; il ne reflète plus l'état courant.

## Par où commencer

| Si tu veux… | Lis |
|---|---|
| Connaître l'avancement réel (fait/en cours/portes externes) | [Checklist de validation](docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md) |
| Comprendre l'architecture | [ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Comprendre le design du slot permanent (Lot 2) | [Spécification](docs/superpowers/specs/2026-08-09-permanent-local-slot-design.md), [Plan d'implémentation](docs/superpowers/plans/2026-08-09-permanent-local-slot-implementation.md) |
| Comprendre l'installateur unique et les mises à jour à distance (Lot 3, en cours) | [LOT3-SETUP-UPDATER.md](docs/operations/LOT3-SETUP-UPDATER.md) |
| Savoir ce qui a été prouvé expérimentalement | [Validation cross-PC](docs/investigation/CROSS-PC-VALIDATION-2026-08.md) et [les preuves](docs/evidence/README.md) |
| Déployer la Phase 3 | [Protocole de déploiement](docs/deployment/PROTOCOLE-DEPLOIEMENT-PHASE3-0.3.0.md) |
| Déployer ou administrer le NAS | [DEPLOYMENT.md](docs/nas/DEPLOYMENT.md), [ADMIN.md](docs/operations/ADMIN.md) |
| Remplacer le monde partagé (procédure Bob → export → import admin) | [REPLACE-PRIMARY-WORLD.md](docs/operations/REPLACE-PRIMARY-WORLD.md) |

## Les deux verrous

Les deux verrous d'écriture ont été **ouverts** après preuves ; de vrais transferts d'hôte ont eu lieu (`Steven → Bob → Steven`, cycles répétés sur `PC-STEVEN`) :

| Verrou | Emplacement | Valeur observée en pilote |
|---|---|---|
| `FeatureGates__AllowHostTransfer` | NAS | `true` |
| `ClientService:EnableWgsTransfer` | PC pilote | `true` |

Le script de build vérifie ces valeurs. Les conditions d'ouverture historiques restent documentées dans [GO-NOGO.md](docs/operations/GO-NOGO.md).

## Ce qui a été prouvé

Sur deux PC physiques (`PC-STEVEN` et `BOBXIME`), avec deux comptes Xbox différents :

- le monde `Shlags1` a effectué un aller-retour complet `Steven → Bob → Steven` avec conservation des inventaires, équipements, positions et progression ;
- le joueur local est celui dont **`id == 0`** — le flag `host` seul ne suffit pas, le jeu le réaffirme sur l'ID 0 à la sauvegarde ;
- un payload est portable vers un **nouveau** `Standard-X` d'une autre machine, jamais les métadonnées WGS ;
- l'opération se déroule sans conflit Xbox Cloud visible, jeu fermé et Internet actif.

Preuves : [`docs/evidence/`](docs/evidence/README.md).

## Prérequis

- Windows 11 x64
- The Planet Crafter installé via Xbox Game Pass
- SDK .NET 10 (`global.json` : `10.0.302`)

## Utilisation — CLI de diagnostic

```powershell
dotnet run --project src/GameSaveHub.Diagnostics -- inventory --json diagnostics-output/inventory.json
dotnet run --project src/GameSaveHub.Diagnostics -- capabilities
dotnet run --project src/GameSaveHub.Diagnostics -- export-world --world Shlags1 --output artifacts
dotnet run --project src/GameSaveHub.Diagnostics -- validate-artifact artifacts/<fichier>.gshsave
dotnet run --project src/GameSaveHub.Diagnostics -- prepare-host --artifact artifacts/<fichier>.gshsave --player "BoB XiMe" --output artifacts/prepared
dotnet run --project src/GameSaveHub.Diagnostics -- import-baseline --output snapshots/import-baselines
dotnet run --project src/GameSaveHub.Diagnostics -- snapshot --output snapshots --test-world Shlags1 --acknowledge-test-world
dotnet run --project src/GameSaveHub.Diagnostics -- validate-snapshot snapshots/<identifiant>
dotnet run --project src/GameSaveHub.Diagnostics -- compare snapshots/<avant>/snapshot-manifest.json snapshots/<apres>/snapshot-manifest.json
```

`inventory` est strictement en lecture seule. `snapshot` refuse de fonctionner si le jeu est actif, si la source change pendant la copie, ou si `--acknowledge-test-world` manque.

`restore-test-world` est réservée au monde jetable : elle refuse toute écriture si une route réseau, le jeu, un hash invalide ou une différence de seed est détecté, et crée un snapshot complet avant tout remplacement atomique.

Pour l'expérience hors ligne guidée sur le PC de référence : `tools/offline-restore-test.ps1`. Le script appelle directement le binaire Release déjà compilé — aucune restauration NuGet ni connexion Internet nécessaire.

## Pilote de transfert d'hôte

Séquence obligatoire :

1. `prepare-host` sur un artefact validé — le pseudo cible doit **déjà exister** dans la sauvegarde.
2. `import-baseline` avant de créer le placeholder local.
3. Créer **un seul** nouveau monde dans Planet Crafter, sauvegarder, fermer le jeu.
4. `import-artifact --player <pseudo> --acknowledge-pilot-import`.

La préparation échange uniquement les IDs joueur nécessaires pour placer le joueur cible en ID 0 / hôte unique. Inventaires, équipements, positions et données de personnage restent attachés à leur objet joueur.

```powershell
dotnet run --project src/GameSaveHub.Diagnostics -- import-artifact `
  --artifact artifacts/prepared/<fichier>.gshsave `
  --baseline snapshots/import-baselines/<id> `
  --player "BoB XiMe" `
  --placeholder GSHIMPORTABC123 `
  --backup-output snapshots/pre-import `
  --acknowledge-pilot-import
```

L'existence de ces commandes ne signifie pas que le transfert d'hôte est ouvert : le feature gate serveur est indépendant.

## Client intégré — Phase 3 / 0.3.0

Apports : installation réelle du service Windows + application WPF, identité ECDSA CNG P-256 machine non exportable, configuration du SID et du profil joueur, catalogue authentifié `GET /api/v1/worlds`, preview sécurisé `GET /api/v1/worlds/{id}/preview`, pseudo Planet Crafter persistant, refus avant transfert si le pseudo n'existe pas exactement une fois dans la sauvegarde.

La couche `GameSaveHub.Client.Orchestration` transforme le pilote en machine d'états persistante côté service Windows : checkpoint atomique, journal d'audit, reprise après interruption, heartbeat serveur et reprise des uploads par chunks. Un crash pendant l'écriture WGS ne déclenche jamais de réécriture automatique.

```text
BUILD-INTEGRATED-PHASE3.cmd
```

Le build compile toute la solution, exécute la suite de tests (153 cas sur `main`), publie le package Windows et génère le contexte Docker de l'API.

Voir [`docs/operations/PHASE3-INTEGRATED-CLIENT.md`](docs/operations/PHASE3-INTEGRATED-CLIENT.md) et [`docs/operations/CLIENT-ORCHESTRATOR-2026-08-07.md`](docs/operations/CLIENT-ORCHESTRATOR-2026-08-07.md).

## Mini-exporteur portable (Lot 1)

Un utilitaire séparé, sans installation ni accès au NAS, permet à un joueur (par ex. Bob) d'exporter sa propre sauvegarde en lecture seule vers un fichier `.gshsave` transmissible :

```text
tools/build-save-exporter.ps1
```

Voir la procédure complète : [REPLACE-PRIMARY-WORLD.md](docs/operations/REPLACE-PRIMARY-WORLD.md).

## Lot 2 en cours — accueil contextuel et slot local permanent

Sur la branche `codex/v1-lot2-contextual-app` (non fusionnée) : IHM contextuelle (« Prendre la main » / « Lancer The Planet Crafter » selon l'état réel du monde), lancement direct de l'application Xbox, et un slot WGS local permanent `GSH-MONDE-PARTAGE` créé une seule fois par PC puis réutilisé (plus de placeholder à chaque prise de main). Détails et avancement réel : [checklist](docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md), [spécification](docs/superpowers/specs/2026-08-09-permanent-local-slot-design.md), [plan](docs/superpowers/plans/2026-08-09-permanent-local-slot-implementation.md).

## Vérification

```powershell
dotnet build GameSaveHub.slnx
dotnet test GameSaveHub.slnx
```

## Organisation du dépôt

```text
src/        projets .NET 10 (client, service, adaptateur, serveur, exporteur portable)
tests/      Unit (153 cas sur main) + EndToEnd
deploy/     compose, Traefik, secrets, stacks Portainer d'administration
tools/      installation client, tests NAS, scripts de build
docs/       architecture, investigation, opérations, déploiement, preuves
_archive/   mémoire du projet, hors Git (archives, contextes de build, conversations)
```

`artifacts/`, `snapshots/` et `diagnostics-output/` sont hors Git mais **ne doivent jamais être purgés** : ils contiennent des preuves de sûreté non régénérables.

Voir aussi [le protocole d'investigation](docs/investigation/PROTOCOL.md), [les constats de la machine initiale](docs/investigation/INITIAL-FINDINGS.md) et [le format `.gshsave`](docs/investigation/ARTIFACT-FORMAT.md).
