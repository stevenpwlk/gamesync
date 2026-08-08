# 03 — Historique du projet

Chronologie reconstituée à partir de `histo/codex.md`, de la conversation ChatGPT partagée, des horodatages des archives, et du contenu des rapports `.gsh*diag`.

## Vue d'ensemble

| Période | Outil | Étape |
|---|---|---|
| 2 août (soir) → 3 août | Codex CLI (`histo/codex.md`) | Conception, investigation mono-PC, serveur complet, déploiement NAS |
| 5 août → 8 août | ChatGPT en ligne | Probe distant, expériences cross-PC, pilote, orchestrateur, Phase 3 |
| 8 août (aujourd'hui) | — | Blocage sur le build Portainer de l'API 0.3.0 |

---

## Phase A — Conception (2 août)

Session Codex ouverte avec le skill `grill-me`. Le dépôt était vide ; un brief produit servait de seule base.

Trois décisions structurantes ont été prises à ce moment, et elles tiennent toujours :

1. **Le transfert d'hôte est un jalon bloquant à prouver**, pas une tâche à implémenter. Microsoft documente que Xbox Game Saves applique sa propre synchronisation cloud et ses propres verrous. En cas d'échec → no-go documenté, pas de mode dégradé.
2. **Stack .NET 10 LTS homogène** (correction faite en séance : .NET 8 arrivait en fin de support le 10 novembre 2026).
3. **Un service Windows comme gardien durable**, mais les opérations Xbox et le lancement visible restent dans la session utilisateur via une application reliée au service — Windows isolant les services en « session 0 ».

Deux pièges réseau ont été anticipés dès la conception : le hairpin NAT de la Livebox (l'URL publique peut ne pas fonctionner depuis le LAN) et l'avertissement SmartScreen pour un installateur non signé Authenticode.

Le plan complet a été rédigé puis exécuté (`PLEASE IMPLEMENT THIS PLAN`), avec 8 phases : Investigation → Go/no-go 2 PC → Prototype local → Serveur minimal → Intégration 2 PC → Déploiement NAS → Pilote 4 joueurs → Généralisation.

## Phase B — Investigation mono-PC (2 → 3 août, nuit)

Le PC de référence : deux sauvegardes, dont **`Shlags1` désignée comme jetable**.

Réalisé :

- `GameSaveHub.Diagnostics` : inventaire WGS strictement en lecture seule ;
- 7 snapshots cohérents créés (`snapshots/`, du 2 août 22:02 au 22:32) ;
- artefact de référence exporté : `20260802T223655Z-...gshsave`, 31 648 octets ;
- **essai de restauration hors ligne** (`tools/offline-restore-test.ps1`, Wi-Fi coupé) sur `Standard-2.json` :
  - hash précédent `025fea99…` → hash restauré `fe969209…` ;
  - snapshot automatique pré-restauration créé ;
  - **résultat confirmé par le joueur** : le jeu a démarré hors ligne, la position précédente est revenue, les deux glaces ajoutées ont disparu.

C'est la première preuve que l'écriture ciblée dans WGS produit l'effet attendu.

## Phase C — Serveur complet et déploiement NAS (3 août)

Construit et déployé dans la même journée :

- API, Infrastructure (4 migrations EF), Admin CLI, DynHost, Traefik, compose Portainer ;
- **33 tests unitaires** au vert ;
- SQLite migrée, mode WAL actif, aucune migration en attente.

Résultat vérifié :

```text
https://saves.stevenpwlk.fr:18443/healthz  →  Healthy
```

Certificat Let's Encrypt de **production** valide (DNS-01 OVH), DynHost actif (`nochg 90.45.82.75`), NAT Livebox `TCP 18443 → NAS:8443`, IP `192.168.1.73` réservée en DHCP statique, port `443` intact pour Home Assistant. **Accès validé depuis un mobile en 5G, Wi-Fi coupé.**

Deux défauts corrigés pendant le déploiement : publication Alpine en `linux-musl-x64`, et accès non-root aux secrets avec propriétaire `100:100` en conservant le mode `600`.

C'est là que s'arrête `histo/codex.md`. **C'est aussi la date où `src/` sur disque a cessé d'être mis à jour.**

## Phase D — Probe distant (5 → 6 août)

Problème à résoudre : le deuxième PC appartient à un ami, à distance, hors LAN. Il fallait un programme **complet et autonome** qu'il puisse lancer seul.

- `GameSaveHub-Probe-Complete-0.2.4-win-x64.zip` (5 août, 59 Mo, self-contained).
- Deux retours d'usage traités : interface en mode sombre illisible (→ passage en mode clair), et saisie manuelle des ressources trop compliquée (→ remplacée par « ramasser 2 items de Glace dans le jeu »).
- Parcours complet validé sur `PC-STEVEN`, puis **sur le PC de l'ami** (`BOBXIME`).

Un incident notable a été traité : l'ami ne pouvait pas lancer le jeu sans Internet, contrairement à Steven — comportement différent qu'il a fallu expliquer avant de lui renvoyer un programme.

## Phase E — Expériences cross-PC (6 → 7 août)

C'est la phase qui a produit les résultats scientifiques du projet.

| Horodatage | Livrable / rapport | Résultat |
|---|---|---|
| 6 août 23:44 | `CrossPc-Safety-Test-0.1.3` | Injection contrôlée d'un artefact d'un PC vers un nouveau `Standard-X` de l'autre |
| 7 août 08:56 | `HostSelectionTest-PC-STEVEN-…085644` | **Découverte : joueur local = ID 0**, `host=true` seul est insuffisant. Cible `Standard-5.json` / `GSHXFER67CC35` |
| 7 août 11:42 | `OnlineCloudTest-PC-STEVEN-…114249` | Remplacement **en ligne**, jeu fermé, Internet actif, surveillance 20 s → `Synchronisé`, aucun conflit visible. Cible `Standard-6.json` / `GSHONLINE5FB4BF` |
| 7 août 13:43 | `BobOnlineSafe-BOBXIME-…134328` | Exécuté sur le PC de l'ami. Issue `SuccessSyncUnclear`, monde importé conservé |
| 7 août 14:24 | `StevenRoundTripSuite-PC-STEVEN-…142410` | **`SuccessSynchronized`** — aller-retour `Steven → Bob → Steven` sur `Shlags1` |

Un retour utilisateur a validé la découverte ID 0 de façon indépendante : *« ton test m'a fait incarner 2 joueurs différents c'est certain, j'ai spawn à 2 endroits différents et j'avais 2 contenus d'inventaire différents »*.

Une exigence de sûreté explicite a été posée par le joueur et intégrée au produit : **ne jamais écraser le `Standard-1.json` existant du PC de l'ami** — d'où la règle « cibler uniquement un nouveau `Standard-X` d'index supérieur à la baseline ».

## Phase F — Consolidation pilote (7 août, après-midi)

`GameSaveHub-Pilot-Consolidation-2026-08-07-r2-source.zip` (15:07).

Les résultats expérimentaux sont transformés en invariants produit :

- `PrepareForHostAsync` — échange d'IDs, refus si pseudo absent ou ambigu, topologie vérifiée ;
- `CreateImportBaselineAsync` — capture WGS complète + hash de tous les mondes + index `Standard-X` maximal ;
- `ImportPortableArtifactAsync` — les 16 invariants d'import, snapshot pré-import et rollback automatique ;
- CLI `prepare-host` / `import-baseline` / `import-artifact` ;
- suppression du fallback dangereux sur le SID du processus service ;
- **43 cas unitaires**.

Révision `r2` : correction de compilation CA1859 dans `PlanetCrafterWorldTransformer`, sans changement fonctionnel.

## Phase G — NAS Phase 2 r2 et orchestrateur (7 août, fin de journée)

| Horodatage | Livrable |
|---|---|
| 15:55 | `Client-Orchestrator-Phase2-r2` |
| 16:16 | `NAS-Phase2-r2-Upgrade` — API **0.2.0** déployée sur le NAS (build Portainer réussi à ce moment-là) |
| 16:40 | `Client-Orchestrator-Phase2-r3` |
| 17:01 | `Network-Auth-Probe-0.1.4` |
| 17:04 | `NetworkAuthProbe-PC-STEVEN-…170441` — enrôlement + challenge ECDSA + JWT validés contre le NAS depuis Windows |
| 17:08 | `REVOKE-TEMP-DEVICE-PORTAINER.yml` — révocation du device de test `bf7e13ed-0ad1-4aca-8ee2-8cd4d3826991` |

L'orchestrateur apporte le checkpoint atomique, le journal d'audit, la reprise après interruption, le heartbeat 30 s et la reprise d'upload par chunks.

## Phase H — Integrated Client Phase 3 / 0.3.0 (8 août)

`GameSaveHub-Integrated-Client-Phase3-0.3.0-r2-source.zip` — dernier fichier interne daté du 8 août 07:58, archive du 8 août 15:14.

Apports :

- installation réelle du service Windows + application WPF (`INSTALL-` / `UNINSTALL-` / `STATUS-GAMESAVEHUB-CLIENT.ps1`) ;
- identité ECDSA CNG P-256 machine non exportable ;
- configuration du SID et du profil joueur ;
- catalogue authentifié `GET /api/v1/worlds` et preview sécurisé ;
- pseudo Planet Crafter persistant, refus avant transfert si le pseudo n'existe pas exactement une fois ;
- **70 tests** ;
- double verrou maintenu fermé des deux côtés.

Puis, à 13:18, `GameSaveHub-Phase3-Deployment-Pack-2026-08-08.zip` : protocole de déploiement en 4 paliers (A : image API, B : validation lecture seule, C : préparation de `Shlags1` sur le NAS, D : vrai client Windows), trois stacks Portainer d'administration, et l'artefact canonique `SHLAGS1-CANONICAL-ROUNDTRIP-20260807.gshsave` (SHA-256 `30af9efc…`).

## Phase I — Le blocage actuel (8 août, après-midi)

Palier A du protocole : construire `gamesavehub-api:0.3.0` dans Portainer.

| Heure | Événement |
|---|---|
| 15:15 | Le build local produit `GameSaveHub-API-0.3.0-Portainer-Build-Context.tar` de **525 498 880 octets** (~501 Mo). Hash `110bbac7…` vérifié, `src/GameSaveHub.Server.Api/Dockerfile` bien présent |
| — | Portainer : `Failure / Unable to build image`, **aucune ligne dans l'onglet Output** |
| — | Cause identifiée : le script empaquette `src/` *après* compilation, donc avec tous les `bin/` et `obj/`. `.dockerignore` les ignorerait pendant le build, mais Portainer doit d'abord recevoir et analyser 501 Mo |
| 16:14 | Contexte propre régénéré : `GameSaveHub-API-0.3.0-Portainer-Build-Context-SLIM.tar`, **583 680 octets** (~570 Ko), SHA-256 `9b6059f9…`, sans aucun `bin/` ni `obj/` |
| — | Portainer : **exactement la même erreur**, toujours sans Output |
| — | Conclusion posée : ce n'est probablement plus GameSave Hub qu'il faut tester, mais Portainer lui-même |

**Dernière action proposée, jamais exécutée :** un smoke test de 10 Ko (`GameSaveHub-Portainer-Build-SmokeTest.tar`, SHA-256 `74a261b8…`) dont le Dockerfile ne fait que :

```dockerfile
FROM gamesavehub-api:0.2.0
LABEL gamesavehub.portainer-build-smoke="true"
```

Aucune compilation, aucun NuGet, aucun téléchargement. Si ce build réussit → le problème vient du contexte 0.3.0. S'il échoue pareil → le problème est dans Portainer / l'endpoint de build du Docker Engine, et il faudra construire directement en SSH sur le NAS pour obtenir de vrais logs.

**La conversation s'arrête là. Aucun résultat n'a été rapporté.**

Pendant tout ce temps, le NAS est resté volontairement intact : `gamesavehub-api:0.2.0` tourne toujours, la stack n'a pas été modifiée.

## État des preuves go/no-go au 8 août

**Acquis :**

- diagnostics des deux PC ;
- mondes jetables identifiés et protégés ;
- import ciblé dans un nouveau `Standard-X` ;
- règle joueur local = ID 0 ;
- conservation inventaires / équipements / positions sur le cycle testé ;
- cycle réel `Steven → Bob → Steven` réussi ;
- fonctionnement en ligne avec Xbox Cloud sans conflit visible sur les essais ;
- portabilité d'une sauvegarde réelle supplémentaire confirmée au niveau payload / slot ID 0.

**Encore requis avant ouverture générale du feature gate :**

- deux cycles A → B → A supplémentaires reproductibles ;
- scénario incluant un redémarrage Windows dans la séquence validée ;
- stratégie documentée lorsqu'un vrai dialogue Local/Cloud apparaît ;
- intégration client/service testée de bout en bout avec rollback ;
- revue finale des logs et du comportement de reprise après interruption.
