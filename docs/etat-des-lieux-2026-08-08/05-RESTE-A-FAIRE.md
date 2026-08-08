# 05 — Reste à faire

## Le blocage actuel : le build Portainer de l'API 0.3.0

C'est le seul point qui empêche tout le reste d'avancer.

**Fait établi :** deux contextes de build très différents (501 Mo puis 570 Ko) produisent **exactement la même erreur générique** `Failure / Unable to build image`, sans aucune ligne dans l'onglet Output de Portainer. Le contexte SLIM a le bon SHA-256 et contient bien `src/GameSaveHub.Server.Api/Dockerfile`. Le Dockerfile 0.3.0 est identique dans sa structure à celui de l'API 0.2.0, qui **avait fonctionné** dans le même Portainer le 7 août.

**Hypothèses restantes, par ordre de probabilité :**

1. La fonction *Build image* de Portainer ou son endpoint vers le Docker Engine est cassée depuis le 7 août (mise à jour, espace disque, permissions, timeout).
2. Le Docker Engine du NAS ne peut plus tirer `mcr.microsoft.com/dotnet/sdk:10.0-alpine` (DNS, proxy, quota Docker Hub / MCR, espace disque).
3. Un problème spécifique au contexte 0.3.0 — devenu improbable après l'échec identique du SLIM.

### Étape 1 — Discriminer (à faire en premier, 5 minutes)

Le smoke test de 10 Ko préparé le 8 août n'a jamais été exécuté. Son Dockerfile ne fait que :

```dockerfile
FROM gamesavehub-api:0.2.0
LABEL gamesavehub.portainer-build-smoke="true"
```

Aucune compilation, aucun NuGet, aucun téléchargement réseau, aucun gros upload.

| Résultat | Conclusion | Suite |
|---|---|---|
| ✅ Build réussi | Portainer va bien, le problème est dans le contexte / le pull des images .NET | Étape 2a |
| ❌ Même erreur | Le problème est Portainer → Docker Engine, pas GameSave Hub | Étape 2b |

> Le fichier `GameSaveHub-Portainer-Build-SmokeTest.tar` **n'est pas présent dans le dépôt**. Il faut le récupérer depuis la conversation ChatGPT, ou le recréer localement (2 lignes de Dockerfile + `tar -cf`).

### Étape 2a — Si le smoke test passe

Tester le pull isolément, puis regarder l'espace disque du NAS. Un `dotnet restore` sur Alpine télécharge plusieurs centaines de Mo de NuGet et de layers.

### Étape 2b — Si le smoke test échoue

Abandonner l'écran *Build image* de Portainer et construire directement sur le NAS en SSH, ce qui donne enfin de vrais logs :

```bash
docker build -t gamesavehub-api:0.3.0 -f src/GameSaveHub.Server.Api/Dockerfile .
```

Contrôles à faire dans la foulée : `df -h` (espace disque), `docker info`, les logs du démon Docker, et la version de Portainer.

### Étape 3 — Corriger le générateur de contexte

Bug confirmé dans `tools/build-integrated-phase3.ps1`, ligne 199 :

```powershell
& tar.exe -cf $apiTar '.dockerignore' 'global.json' 'Directory.Build.props' 'src'
```

L'appel se fait à l'étape 6/6, donc après compilation : `src/**/bin` et `src/**/obj` sont déjà pleins et `tar` ne connaît pas `.dockerignore`. À remplacer par une copie filtrée dans un répertoire temporaire, ou par `tar --exclude`. Un garde-fou refusant tout contexte > 20 Mo ou contenant encore `bin`/`obj` avait été proposé — il vaut la peine d'être intégré au script plutôt que gardé à part.

---

## Une fois débloqué : le protocole de déploiement Phase 3

Les quatre paliers sont déjà écrits (`GameSaveHub-Phase3-Deployment-Pack-2026-08-08.zip` → `PROTOCOLE-DEPLOIEMENT-PHASE3-0.3.0.md`). Ils sont à suivre dans l'ordre, sans sauter d'étape.

### Palier A — Image API 0.3.0

Build dans Portainer, `gamesavehub-api:0.3.0`, Dockerfile `src/GameSaveHub.Server.Api/Dockerfile`.

**Avant la mise à jour de stack :** arrêter uniquement `gamesavehub-api-1`, puis copier tout `/Volume2/gamesavehub/data/` vers `/Volume2/gamesavehub/backups/pre-api-0.3.0-20260808/` via le File Manager TOS.

Puis remplacer **uniquement** la ligne `image:` dans l'éditeur de stack. Vérifier impérativement `FeatureGates__AllowHostTransfer: "false"`. Attendre `healthy`. Contrôler les logs : pas de crash loop, pas de migration SQLite en attente, pas d'erreur d'accès `/data`.

**Ne pas supprimer `gamesavehub-api:0.2.0`** — c'est le rollback, et il est direct puisque la Phase 3 n'ajoute aucune migration.

### Palier B — Validation lecture seule

```bash
powershell -ExecutionPolicy Bypass -File .\TEST-NAS-PHASE3-READONLY.ps1
```

Attendu : DNS OK, TCP 18443 OK, `/healthz` = 200 `Healthy`, `/api/v1/worlds` sans JWT = **401**, `/api/v1/worlds/<GUID>/preview` sans JWT = **401**. Le test n'écrit ni dans SQLite ni dans WGS.

### Palier C — Préparer `Shlags1` sur le NAS

1. Déposer `SHLAGS1-CANONICAL-ROUNDTRIP-20260807.gshsave` dans `/Volume2/gamesavehub/imports/` — **jamais** directement dans `/data/objects`.
2. Lister les mondes existants (`ADMIN-WORLD-LIST-PORTAINER.yml`), lire les logs, supprimer la stack. Ne rien créer avant d'avoir lu cette sortie.
3. Si aucun `Shlags1` n'existe : `ADMIN-CREATE-SHLAGS1-PORTAINER.yml`, conserver le GUID affiché, supprimer la stack.
4. Import initial : renseigner le GUID dans `ADMIN-IMPORT-SHLAGS1-PORTAINER.template.yml`, déployer. SHA-256 attendu dans les logs :
   `30af9efca4bed6b7042c7dae4f83fedaa8fc9311c22153735d3a00fc96d76495`
5. Revérifier avec `ADMIN-WORLD-LIST` : `Shlags1` doit afficher un `CurrentVersionId`.

### Palier D — Vrai client Windows sur PC-STEVEN

Installer `GameSaveHub-Client-Phase3-0.3.0-win-x64.zip` via `INSTALL-GAMESAVEHUB-CLIENT.ps1` en administrateur. Vérifier : utilisateur interactif résolu, SID résolu, bon `AppData\Local`, service `GameSaveHubClient` en `Running`, `EnableWgsTransfer=false`.

Puis, depuis l'application : créer une invitation via l'admin NAS, s'associer avec le pseudo `Stevenpwlk`, charger les mondes, sélectionner `Shlags1`, ouvrir l'aperçu, lancer le préflight.

Attendu : trois joueurs visibles, `Stevenpwlk` présent exactement une fois, statut `preflight_ready`, **bouton de transfert désactivé**.

Test négatif : basculer temporairement sur un pseudo inexistant → `player_not_found`, puis remettre `Stevenpwlk`.

**Aucune écriture WGS ne doit avoir lieu pendant toute la Phase 3.**

---

## Phase 4 — Ouvrir le transfert pour le monde pilote

À n'entamer qu'après un Palier D intégralement vert.

L'ouverture se fera **d'abord pour le seul monde pilote**, avec les deux verrous ouverts explicitement et séparément.

### Preuves manquantes avant ouverture générale

| # | Preuve requise | État |
|---|---|---|
| 1 | Deux cycles A → B → A supplémentaires reproductibles | ❌ 1 seul cycle réussi (7 août) |
| 2 | Scénario incluant un redémarrage Windows dans la séquence | ❌ Jamais testé |
| 3 | Stratégie documentée face à un vrai dialogue Local/Cloud | ❌ Jamais rencontré, donc jamais documenté |
| 4 | Intégration client/service testée de bout en bout avec rollback | ❌ Bloquée par le Palier A |
| 5 | Revue finale des logs et du comportement de reprise après interruption | ❌ À faire |

Les huit conditions historiques du `GO-NOGO.md` restent la référence : rapport Probe du PC distant ✅, mondes jetables identifiés ✅, snapshots cohérents ✅, trois cycles A→B→A ❌, identités/inventaires/positions/hôte conservés ✅, redémarrage Windows inclus ❌, Xbox Cloud connecté et conflits simulés ⚠️ (connecté oui, conflit jamais provoqué), aucun remplacement cloud indéterministe ✅.

**Un échec sur cette liste impose le no-go documenté.** C'était la règle posée au premier jour.

---

## Tâches de nettoyage opérationnel en attente

Indépendantes du blocage Portainer, toutes documentées dans `CLEANUP-TEST-ASSETS-2026-08-07.md`.

### Sur PC-STEVEN — supprimer 6 mondes de test

**Uniquement depuis l'interface de The Planet Crafter, Internet actif.** Jamais via l'Explorateur, PowerShell ou `%LOCALAPPDATA%`.

| Logique | Nom affiché |
|---|---|
| `Standard-3.json` | `GSHDIAG55E319` |
| `Standard-4.json` | `GSHDIAG213E59` |
| `Standard-5.json` | `GSHXFER67CC35` |
| `Standard-6.json` | `GSHDIAGF6710B` |
| `Standard-7.json` | `GSH-SHLAGS-RETURN` |
| `Standard-8.json` | `GSH-BOB-REAL-WORLD` |

**À conserver absolument :** `Standard-1` (monde historique) et `Shlags1` (`Standard-2.json`).

### Sur le NAS — révoquer le device de test

DeviceId `bf7e13ed-0ad1-4aca-8ee2-8cd4d3826991`, via la stack `REVOKE-TEMP-DEVICE-PORTAINER.yml` (déjà présente à la racine du dépôt). Vérifier dans les logs : `Appareil bf7e13ed-… révoqué.` puis supprimer la stack.

### Sur le NAS — supprimer les stacks temporaires

`gamesavehub-enrollment-temp` et `gamesavehub-revoke-temp-device`, ainsi que leurs conteneurs one-shot arrêtés.

**Ne jamais** utiliser `docker system prune --volumes`. Ne pas supprimer les images de rollback (`gamesavehub-api:0.1.0` et `0.2.0`) même marquées *unused*.

### PC de Bob — ne rien faire pour l'instant

Ses mondes temporaires servent de copie de secours tant que le premier transfert intégré `Shlags1` n'est pas stocké et protégé sur le NAS.

---

## Dette technique identifiée

| Sujet | Détail |
|---|---|
| `build-integrated-phase3.ps1` ligne 199 | Empaquette `bin/`+`obj/` dans le contexte Docker |
| `GameSaveHub.slnx` sur disque | Il manque `GameSaveHub.Client.Orchestration` |
| `ARTIFACT-FORMAT.md` | Ratio de compression documenté à 10, codé à 100 |
| `deploy/compose.portainer.yml` sur disque | Encore sur `gamesavehub-api:0.1.0` |
| Installateur Windows | Non signé Authenticode → avertissement SmartScreen, connu et assumé depuis la conception |
| Numérotation des phases | Deux systèmes concurrents (voir [04](04-ETAT-DU-DEPOT.md), anomalie 6) |

## Ce qui n'a jamais été commencé

Éléments du plan initial encore intégralement à faire, tous postérieurs à la Phase 4 :

- mise à jour client par manifeste ECDSA signé + vérification SHA-256 ;
- pilote à 4 joueurs et import du monde principal (aujourd'hui : 2 joueurs, monde jetable) ;
- SDK d'adaptateurs et chargement dynamique (explicitement reporté après stabilisation de Planet Crafter) ;
- tests opérationnels de restauration : reconstruction après perte de SQLite, restauration d'archive validée sur dossier isolé.
