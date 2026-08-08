# GameSave Hub

Partager en toute sûreté un monde multijoueur **The Planet Crafter** (PC Game Pass) entre plusieurs PC Windows 11 avec des comptes Xbox différents, chacun pouvant devenir hôte à son tour et retrouver le point exact où les autres se sont arrêtés.

**État au 8 août 2026 : Integrated Client Phase 3 / 0.3.0 r2 — 70/70 tests, les deux verrous d'écriture restent fermés.**

> 📍 **Point de blocage en cours :** le build de l'image `gamesavehub-api:0.3.0` échoue dans Portainer (`Failure / Unable to build image`, sans log). Le NAS tourne toujours sur `gamesavehub-api:0.2.0`, stack intacte. Détail et marche à suivre : [`docs/etat-des-lieux-2026-08-08/05-RESTE-A-FAIRE.md`](docs/etat-des-lieux-2026-08-08/05-RESTE-A-FAIRE.md).

## Par où commencer

| Si tu veux… | Lis |
|---|---|
| Comprendre où en est le projet | [État des lieux complet](docs/etat-des-lieux-2026-08-08/README.md) |
| Comprendre l'architecture | [ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Savoir ce qui a été prouvé expérimentalement | [Validation cross-PC](docs/investigation/CROSS-PC-VALIDATION-2026-08.md) et [les preuves](docs/evidence/README.md) |
| Déployer la Phase 3 | [Protocole de déploiement](docs/deployment/PROTOCOLE-DEPLOIEMENT-PHASE3-0.3.0.md) |
| Connaître les conditions d'ouverture du transfert | [GO-NOGO.md](docs/operations/GO-NOGO.md) |
| Déployer ou administrer le NAS | [DEPLOYMENT.md](docs/nas/DEPLOYMENT.md), [ADMIN.md](docs/operations/ADMIN.md) |

## Les deux verrous

Aucun transfert réel n'est possible aujourd'hui. Deux verrous **indépendants** sont fermés :

| Verrou | Emplacement | Valeur |
|---|---|---|
| `FeatureGates__AllowHostTransfer` | NAS | `false` |
| `ClientService:EnableWgsTransfer` | PC | `false` |

Le script de build vérifie ces deux valeurs et refuse de compiler si elles ont été modifiées. Leur ouverture est conditionnée aux preuves listées dans [GO-NOGO.md](docs/operations/GO-NOGO.md).

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

Le build compile toute la solution, exécute 70 cas de test, publie le package Windows et génère le contexte Docker de l'API 0.3.0.

Voir [`docs/operations/PHASE3-INTEGRATED-CLIENT.md`](docs/operations/PHASE3-INTEGRATED-CLIENT.md) et [`docs/operations/CLIENT-ORCHESTRATOR-2026-08-07.md`](docs/operations/CLIENT-ORCHESTRATOR-2026-08-07.md).

## Vérification

```powershell
dotnet build GameSaveHub.slnx
dotnet test GameSaveHub.slnx
```

## Organisation du dépôt

```text
src/        13 projets .NET 10
tests/      Unit (70 cas) + EndToEnd
deploy/     compose, Traefik, secrets, stacks Portainer d'administration
tools/      installation client, tests NAS, scripts de build
docs/       architecture, investigation, opérations, déploiement, preuves
_archive/   mémoire du projet, hors Git (archives, contextes de build, conversations)
```

`artifacts/`, `snapshots/` et `diagnostics-output/` sont hors Git mais **ne doivent jamais être purgés** : ils contiennent des preuves de sûreté non régénérables.

Voir aussi [le protocole d'investigation](docs/investigation/PROTOCOL.md), [les constats de la machine initiale](docs/investigation/INITIAL-FINDINGS.md) et [le format `.gshsave`](docs/investigation/ARTIFACT-FORMAT.md).
