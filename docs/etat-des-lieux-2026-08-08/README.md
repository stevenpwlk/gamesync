# GameSave Hub — État des lieux complet (8 août 2026)

Audit réalisé le 8 août 2026 sur `C:\Users\steve\OneDrive\Code\gamesync`.

Sources analysées :

- l'intégralité du dépôt sur disque (`src/`, `tests/`, `docs/`, `deploy/`, `tools/`, `artifacts/`, `snapshots/`, `diagnostics-output/`) ;
- les 10 archives sources et 3 archives binaires présentes à la racine ;
- les 2 contextes de build Docker (`.tar`) présents à la racine ;
- les 5 rapports de diagnostic (`.gsh*diag`) ;
- `histo/codex.md` (29 472 lignes) — session Codex CLI du 2 au 3 août ;
- la conversation ChatGPT partagée du 5 au 8 août (`https://chatgpt.com/share/6a773bb5-91fc-83ed-964e-6d8ef96efb6f`).

## Sommaire

| Document | Contenu |
|---|---|
| [01 — Contexte et produit](01-CONTEXTE-ET-PRODUIT.md) | Le but réel du projet, les contraintes Xbox/WGS, les règles de sûreté non négociables |
| [02 — Architecture et code](02-ARCHITECTURE-ET-CODE.md) | Les 13 projets .NET, l'API, la machine d'états, les formats, les tests |
| [03 — Historique](03-HISTORIQUE.md) | Chronologie complète du 2 au 8 août, expériences et preuves obtenues |
| [04 — État du dépôt](04-ETAT-DU-DEPOT.md) | Inventaire fichier par fichier et les 12 anomalies constatées |
| [05 — Reste à faire](05-RESTE-A-FAIRE.md) | Blocage actuel, jalons restants, critères go/no-go |
| [06 — Plan de rangement](06-PLAN-DE-RANGEMENT.md) | Proposition de tri du dépôt, cible et séquence d'exécution |
| [07 — Revue critique du code](07-REVUE-CRITIQUE.md) | Défauts vérifiés dans les sources et remises en question d'architecture |
| [08 — Diagnostic du NAS](08-DIAGNOSTIC-NAS.md) | Pourquoi le build Portainer échouait, et l'état réel du NAS |

---

## Résumé exécutif

### Ce qui est fait

Le projet a dépassé de très loin la « Phase 0 » que décrit le `README.md` racine. En six jours :

1. **Le serveur est en production.** `https://saves.stevenpwlk.fr:18443/healthz` répond `Healthy` derrière Traefik, avec un certificat Let's Encrypt de production (DNS-01 OVH), DynHost OVH pour l'IPv4 dynamique, NAT Livebox `18443 → NAS:8443`, et accès validé depuis le LAN **et** depuis un mobile en 5G. L'image active sur le NAS est `gamesavehub-api:0.2.0`.
2. **La faisabilité technique du transfert d'hôte est prouvée.** Sur deux PC réels (`PC-STEVEN` et `BOBXIME`, comptes Xbox différents), le monde `Shlags1` a effectué un aller-retour complet `Steven → Bob → Steven` avec conservation des inventaires, équipements, positions et progression, jeu **en ligne**, sans conflit Xbox Cloud visible.
3. **La règle centrale a été découverte expérimentalement** : le joueur local est celui dont `id == 0`, pas celui marqué `host=true`. Le jeu réaffirme `host` sur l'ID 0 à la sauvegarde. Préparer un autre hôte consiste donc à **échanger les IDs joueur**.
4. **Le pilote est implémenté et testé** : `prepare-host`, `import-baseline`, `import-artifact` dans l'adapter + CLI, un orchestrateur client persistant avec reprise après crash, un service Windows, une application WPF, l'identité machine ECDSA P-256 CNG non exportable, et un catalogue de mondes authentifié. **70 tests unitaires passent.**

### Ce qui bloque, maintenant

**Le build de l'image `gamesavehub-api:0.3.0` échoue dans Portainer** avec un message générique `Failure / Unable to build image`, sans aucune ligne dans l'onglet Output.

- Première cause identifiée et corrigée : le script de build produisait un contexte de **501 Mo** (il empaquetait `src/` *après* compilation, donc avec tous les `bin/` et `obj/`).
- Le contexte propre de **570 Ko** (`GameSaveHub-API-0.3.0-Portainer-Build-Context-SLIM.tar`, présent à la racine, SHA-256 vérifié conforme) **échoue exactement pareil**.
- La dernière étape proposée — et **jamais exécutée** — est un smoke test Portainer de 10 Ko (`GameSaveHub-Portainer-Build-SmokeTest.tar`) pour déterminer si c'est la fonction *Build image* de Portainer qui est en cause, et non GameSave Hub.

Tant que ce point n'est pas tranché, **rien de la Phase 3 ne peut être déployé**, et les paliers B, C et D du protocole de déploiement sont à l'arrêt.

### Le vrai problème du dépôt

**Le dépôt Git contient zéro commit.** Rien n'est suivi, il n'y a ni branche, ni remote, ni historique.

Conséquence directe : **le répertoire de travail n'est pas la source de vérité.** `src/`, `docs/` et `deploy/` sur disque sont figés au 3 août (API 0.1.0). Les cinq jours de travail suivants — l'orchestrateur client, le transformateur de mondes, la vérification joueur, l'API 0.3.0, les six documents opérationnels — **n'existent que dans les ZIP à la racine**.

C'est le risque numéro un du projet : plus grave que le blocage Portainer, car un ZIP supprimé par erreur détruit du travail irremplaçable.

### Les trois actions prioritaires

| # | Action | Pourquoi |
|---|---|---|
| 1 | Promouvoir `GameSaveHub-Integrated-Client-Phase3-0.3.0-r2-source.zip` comme arbre de travail, puis **faire le premier commit Git** | Arrête l'hémorragie : le travail du 5 au 8 août n'est protégé par rien |
| 2 | Exécuter le smoke test Portainer 10 Ko | Débloque tout le reste ; sépare « problème GameSave Hub » de « problème Portainer/Docker » |
| 3 | Corriger `tools/build-integrated-phase3.ps1` ligne 199 pour exclure `bin/`/`obj/` du `.tar` | Le bug est confirmé dans le code, il se reproduira à chaque build |

Le détail est dans [05 — Reste à faire](05-RESTE-A-FAIRE.md) et [06 — Plan de rangement](06-PLAN-DE-RANGEMENT.md).
