# 04 — État du dépôt

> ⚠️ **Archive figée au 8-9 août 2026, non mise à jour.** Pour l'état réel actuel du projet, voir [`docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`](../operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md).

Photographie prise le 8 août 2026 sur `C:\Users\steve\OneDrive\Code\gamesync`.

## Inventaire de la racine

35 entrées, dont 25 fichiers qui n'ont rien à faire à la racine d'un dépôt de code.

### Fichiers de projet légitimes

| Fichier | Taille | Date | Remarque |
|---|---|---|---|
| `GameSaveHub.slnx` | 1 394 o | 3 août | **Périmé** : 12 projets, il en manque un (`Client.Orchestration`) |
| `global.json` | 109 o | 2 août | SDK .NET `10.0.302` |
| `Directory.Build.props` | 282 o | 2 août | OK |
| `.gitignore` | 161 o | 3 août | OK |
| `.dockerignore` | 134 o | 3 août | OK |
| `README.md` | 2 679 o | 3 août | **Périmé** : annonce « Phase 0 », état d'il y a 5 jours |

### Archives sources (10 fichiers, ~1,5 Mo)

| Archive | Taille | Date | Rôle |
|---|---|---|---|
| `GameSaveHub-CrossPc-Safety-Test-0.1.3-source.zip` | 182 790 o | 6 août 23:44 | Expérience |
| `GameSaveHub-Host-Selection-Test-0.1.0-source.zip` | 190 123 o | 7 août 08:50 | Expérience |
| `GameSaveHub-Online-Cloud-Safety-Test-0.1.1-source.zip` | 182 425 o | 7 août 11:32 | Expérience |
| `GameSaveHub-Bob-Online-Safe-0.2.1-source.zip` | 72 789 o | 7 août 13:33 | Expérience (PC distant) |
| `GameSaveHub-Steven-RoundTrip-Suite-0.1.0-source.zip` | 151 124 o | 7 août 14:12 | Expérience |
| `GameSaveHub-Pilot-Consolidation-2026-08-07-r2-source.zip` | 145 662 o | 7 août 15:07 | Jalon produit |
| `GameSaveHub-Client-Orchestrator-Phase2-r2-…zip` | 166 740 o | 7 août 15:55 | Jalon produit (superseded par r3) |
| `GameSaveHub-Client-Orchestrator-Phase2-r3-…zip` | 167 000 o | 7 août 16:40 | Jalon produit |
| `GameSaveHub-NAS-Phase2-r2-Upgrade-2026-08-07.zip` | 75 023 o | 7 août 16:16 | Pack de déploiement API 0.2.0 |
| `GameSaveHub-Network-Auth-Probe-0.1.4-source.zip` | 14 878 o | 7 août 17:01 | Outil de validation réseau |
| **`GameSaveHub-Integrated-Client-Phase3-0.3.0-r2-source.zip`** | **181 859 o** | **8 août 15:14** | **⭐ Source de vérité actuelle** |
| `GameSaveHub-Phase3-Deployment-Pack-2026-08-08.zip` | 13 022 o | 8 août 15:19 | Protocole + stacks admin + artefact canonique |

### Archives binaires (3 fichiers, 178 Mo)

| Archive | Taille | Date |
|---|---|---|
| `GameSaveHub-Probe-Complete-0.2.4-win-x64.zip` | 59 249 969 o | 5 août 12:40 |
| `GameSaveHub-Bob-Import-Trial-0.1.1-win-x64.zip` | 59 254 058 o | 7 août 10:23 |
| `GameSaveHub-Bob-Online-Safe-0.2.1-win-x64.zip` | 59 259 329 o | 7 août 13:34 |

Ce sont des publications self-contained .NET, reconstructibles depuis les sources correspondantes. Elles pèsent **à elles seules plus que tout le reste du dépôt hors `bin/obj`**.

### Contextes de build Docker (2 fichiers)

| Fichier | Taille | SHA-256 vérifié | État |
|---|---|---|---|
| `GameSaveHub-API-0.2.0-Portainer-Build-Context.tar` | 542 720 o | `8a4e92e5…` | Build réussi le 7 août, image en production |
| `GameSaveHub-API-0.3.0-Portainer-Build-Context-SLIM.tar` | 583 680 o | `9b6059f9…` ✅ conforme | **Build échoué**, cause inconnue |

Le SHA-256 du contexte SLIM correspond exactement à la valeur attendue. Le fichier est sain ; le problème est ailleurs.

### Rapports de diagnostic (5 fichiers, ~1 Mo)

Ce sont des archives ZIP contenant `manifest.json` + preuves. **Ce sont des données expérimentales irremplaçables.**

| Fichier | Machine | Issue |
|---|---|---|
| `…HostSelectionTest-PC-STEVEN-20260807-085644-628b07.gshhostdiag` | PC-STEVEN | Découverte règle ID 0 |
| `…OnlineCloudTest-PC-STEVEN-20260807-114249-023b09.gshclouddiag` | PC-STEVEN | Xbox Cloud en ligne OK |
| `…BobOnlineSafe-BOBXIME-20260807-134328-fa810a.gshbobdiag` | BOBXIME | `SuccessSyncUnclear` |
| `…StevenRoundTripSuite-PC-STEVEN-20260807-142410-64e66a.gshroundtripdiag` | PC-STEVEN | `SuccessSynchronized` |
| `…NetworkAuthProbe-PC-STEVEN-20260807-170441.gshnetdiag` | PC-STEVEN | Auth NAS validée |

### Documents et divers

| Fichier | Taille | Date | Remarque |
|---|---|---|---|
| `project.md` | 7 095 o | 6 août 10:48 | Synthèse IA, partiellement périmée |
| `project-detailed.md` | 8 308 o | 6 août 10:49 | Redondant avec `project.md` |
| `plan_detaillé.md` | 9 704 o | 6 août 12:08 | Numérotation de phases **incompatible** avec la réalité |
| `tests_et_validation.md` | 3 261 o | 6 août 12:54 | Conclusions erronées (voir anomalie 7) |
| `REVOKE-TEMP-DEVICE-PORTAINER.yml` | 488 o | 7 août 17:08 | Stack one-shot, appartient à `deploy/` |
| `Modelfile` | 45 o | 6 août 16:58 | `FROM qwen3-coder:30b` — sans rapport avec le projet |

### Répertoires

| Répertoire | Taille | Contenu |
|---|---|---|
| `src/` | **422 Mo** | ~410 Mo de `bin/` + `obj/`, code figé au 3 août |
| `tests/` | **128 Mo** | idem |
| `artifacts/` | 229 Mo | sorties de build + preuves d'expérience |
| `histo/` | 1,3 Mo | `codex.md` (29 472 lignes) + un `.url` |
| `snapshots/` | 970 Ko | 7 captures WGS du 2 août — **preuves de sûreté** |
| `docs/` | 48 Ko | 7 documents, il en manque 6 |
| `deploy/` | 25 Ko | compose + traefik + secrets (README seul) |
| `diagnostics-output/` | 20 Ko | 3 rapports JSON du 3 août |
| `tools/` | 16 Ko | 2 scripts, il en manque 4 |

---

## Les 12 anomalies

### 1. 🔴 Zéro commit Git

```text
fatal: your current branch 'master' does not have any commits yet
git ls-files → 0
```

Aucune branche, aucun remote, aucun historique. Le `.gitignore` existe mais ne protège rien puisque rien n'est suivi.

**Impact :** aucune traçabilité, aucun retour arrière, aucune sauvegarde hors OneDrive. Toute erreur de manipulation est définitive.

### 2. 🔴 Le répertoire de travail n'est pas la source de vérité

`src/`, `docs/`, `deploy/` sur disque sont figés au **3 août 11:20**. Le dernier fichier modifié dans l'arbre de travail est `tools/TEST-NAS-PHASE2-R2-READONLY.ps1` (7 août 16:33), copié à la main.

Fichiers présents dans l'archive Phase 3 et **absents du disque** :

```text
BUILD-INTEGRATED-PHASE3.cmd
SOURCE-SHA256SUMS.txt
docs/investigation/CROSS-PC-VALIDATION-2026-08.md
docs/operations/CLEANUP-TEST-ASSETS-2026-08-07.md
docs/operations/CLIENT-ORCHESTRATOR-2026-08-07.md
docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md
docs/operations/PHASE3-INTEGRATED-CLIENT.md
docs/operations/PILOT-CONSOLIDATION-2026-08-07.md
src/GameSaveHub.Adapters.PlanetCrafter.GamePass/PlanetCrafterWorldTransformer.cs
src/GameSaveHub.Client.Orchestration/          (6 fichiers, dont TransferOrchestrator.cs ~42 Ko)
src/GameSaveHub.Client.Service/AuthenticatedTransferServerClient.cs
src/GameSaveHub.Client.Service/RegisteredUserProfileResolver.cs
src/GameSaveHub.Client.Service/TransferHeartbeatWorker.cs
src/GameSaveHub.Client.Service/TransferRecoveryWorker.cs
src/GameSaveHub.Contracts/PlayerCompatibilityRules.cs
tests/Unit/PlayerCompatibilityRulesTests.cs
tests/Unit/TransferOrchestratorTests.cs
tools/INSTALL-GAMESAVEHUB-CLIENT.ps1
tools/STATUS-GAMESAVEHUB-CLIENT.ps1
tools/UNINSTALL-GAMESAVEHUB-CLIENT.ps1
tools/build-integrated-phase3.ps1
```

S'y ajoutent des fichiers **modifiés** non reportés : `PlanetCrafterGamePassAdapter.cs`, `Server.Api/Program.cs`, `Diagnostics/Program.cs`, `MainWindow.xaml(.cs)`, `PipeServerWorker.cs`, `ArtifactEnvelopeValidator.cs`, `ApiContracts.cs`, les composes, et 5 des 8 fichiers de tests.

**C'est le risque numéro un du projet.** Il est plus grave que le blocage Portainer.

### 3. 🟠 Racine polluée par 178 Mo de binaires reconstructibles

Trois ZIP `win-x64` self-contained. Ils sont utiles comme livrables historiques mais n'ont pas leur place à la racine, et surtout pas dans un futur dépôt Git.

### 4. 🟠 410 Mo de `bin/` et `obj/` sur disque — et ils ont cassé le build

Ils sont bien couverts par `.gitignore` **et** `.dockerignore`. Mais `tools/build-integrated-phase3.ps1` ligne 199 les empaquette quand même :

```powershell
& tar.exe -cf $apiTar '.dockerignore' 'global.json' 'Directory.Build.props' 'src'
```

Cet appel arrive à l'étape **6/6**, donc après `dotnet build` (étape 2) et après la publication du client (étape 5). `tar` n'a évidemment aucune notion de `.dockerignore`. Résultat : un contexte de 501 Mo au lieu de 570 Ko, et le premier échec Portainer.

**Bug confirmé dans le code, à corriger : il se reproduira à chaque build.**

### 5. 🟠 `GameSaveHub.slnx` incomplet

Le fichier sur disque déclare 12 projets. Celui de l'archive Phase 3 en déclare 13 — il manque `src/GameSaveHub.Client.Orchestration/GameSaveHub.Client.Orchestration.csproj`. Un `dotnet build` sur l'arbre actuel ne compile donc pas l'orchestrateur.

### 6. 🟠 Quatre documents de planification concurrents et divergents

`README.md` (3 août), `project.md`, `project-detailed.md`, `plan_detaillé.md`, `tests_et_validation.md` (tous du 6 août) décrivent quatre états différents du même projet.

Divergences concrètes :

- `README.md` et `project.md` affirment qu'**aucune fonction d'import n'est activée** — c'est faux depuis le 7 août : `prepare-host`, `import-baseline` et `import-artifact` existent (le verrou est ailleurs, dans les feature gates).
- `plan_detaillé.md` numérote 11 phases (« Phase 5 : Import/Export », « Phase 6 : Transfert d'hôte ») qui **ne correspondent à rien** dans la numérotation réellement utilisée par les documents opérationnels (Phase 0 / Phase 2 orchestrateur / Phase 3 integrated client / Phase 4 à venir). Deux systèmes de numérotation coexistent, c'est une source de confusion garantie.
- `plan_detaillé.md` parle de « tests multi-joueurs simulés sur votre PC unique », alors que la validation réelle s'est faite sur deux PC physiques distincts depuis le 6 août.

### 7. 🟠 `tests_et_validation.md` tire des conclusions fausses

Le document conclut de deux `404` sur `/api/healthz` et `/api/auth/status` que « les endpoints spécifiques de l'API ne sont pas accessibles ». Ces routes **n'existent tout simplement pas** : l'API expose `/healthz` et `/api/v1/...`. Les mêmes `404` apparaissent d'ailleurs dans les logs Traefik du 6 août.

Conservé tel quel, ce document enverra un futur lecteur sur une fausse piste.

### 8. 🟡 `docs/` sur disque amputé de 6 documents

Les six documents opérationnels et d'investigation les plus importants du projet (cross-PC, consolidation pilote, orchestrateur, checklist, Phase 3, nettoyage) n'existent que dans les archives.

### 9. 🟡 `deploy/compose.portainer.yml` désynchronisé du NAS

| Source | Image API déclarée |
|---|---|
| `deploy/compose.portainer.yml` sur disque | `gamesavehub-api:0.1.0` |
| Réalité sur le NAS | `gamesavehub-api:0.2.0` |
| Archive Phase 3 | `gamesavehub-api:0.3.0` (cible) |

Le fichier de déploiement du dépôt a **deux versions de retard**. `FeatureGates__AllowHostTransfer: "false"` est correct partout, heureusement.

### 10. 🟡 `histo/GPT_histo1.url` pointe vers une autre conversation

Le raccourci contient :

```text
https://chatgpt.com/share/6a744bab-12bc-83ed-9c32-16e95bda1324
```

alors que la conversation de référence pour la suite du projet est :

```text
https://chatgpt.com/share/6a773bb5-91fc-83ed-964e-6d8ef96efb6f
```

Deux liens distincts, un seul est référencé dans le dépôt, et rien n'indique lequel couvre quoi.

### 11. 🟡 `Modelfile` orphelin

`FROM qwen3-coder:30b` + `num_ctx 32768`. C'est une configuration Ollama sans rapport avec GameSave Hub.

### 12. 🟡 Écarts mineurs doc / code

- `ARTIFACT-FORMAT.md` annonce un refus au-delà d'un ratio de compression de **10** ; le code utilise **100**.
- `PILOT-CONSOLIDATION` annonce 43 cas de test, `PHASE3-INTEGRATED-CLIENT` en annonce 70 : les deux sont justes à leur date, mais aucun document ne dit lequel fait foi aujourd'hui.

---

## Ce qui est sain

Il faut le dire aussi, parce que ça conditionne le plan de rangement :

- ✅ **Aucun secret dans le dépôt.** `deploy/secrets/` ne contient qu'un `README.md`, et `.gitignore` couvre `.env`, `*.key`, `*.pem`, `*.pfx` et `deploy/secrets/*`.
- ✅ `.gitignore` et `.dockerignore` sont corrects et bien pensés.
- ✅ Le contexte de build SLIM a le bon SHA-256 : le fichier n'est pas corrompu.
- ✅ Les preuves expérimentales (`snapshots/`, `.gsh*diag`, `artifacts/transfer-*`) sont toutes présentes.
- ✅ Le NAS est resté dans un état stable et connu — rien n'a été cassé en production.

> **Note de confidentialité, sans gravité mais à connaître :** la conversation ChatGPT partagée est publique par lien et contient le `compose.yml` complet, `saves.stevenpwlk.fr`, l'IPv4 publique `90.45.82.75`, l'adressage LAN `192.168.1.x`, les chemins NAS et une adresse e-mail. Aucun secret n'y figure (les secrets sont montés par fichier), mais c'est une cartographie détaillée de l'infrastructure. À révoquer si le partage n'est plus nécessaire.
