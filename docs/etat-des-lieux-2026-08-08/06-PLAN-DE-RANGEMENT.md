# 06 — Plan de rangement du dépôt

> ⚠️ **Archive figée au 8-9 août 2026, non mise à jour.** Pour l'état réel actuel du projet, voir [`docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`](../operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md).

> **Proposition.** Rien de ce qui suit n'a été exécuté. Aucune suppression n'est faite sans validation explicite.

## Principe directeur

Le dépôt mélange aujourd'hui quatre natures de fichiers très différentes, traitées de la même façon :

| Nature | Exemple | Traitement souhaitable |
|---|---|---|
| **Code vivant** | `src/`, `tests/`, `deploy/`, `tools/` | Suivi par Git, source de vérité unique |
| **Preuves expérimentales** | `.gsh*diag`, `snapshots/`, `artifacts/transfer-*` | Conservées, jamais régénérables |
| **Livrables historiques** | les 13 archives ZIP/TAR | Conservés hors Git, consultables |
| **Déchets** | `bin/`, `obj/`, `Modelfile`, docs périmés | Supprimables |

Le rangement consiste à séparer ces quatre familles, et surtout à **rétablir une source de vérité unique**.

## Arborescence cible

```text
gamesync/
├── .gitignore  .gitattributes  .dockerignore
├── global.json  Directory.Build.props
├── GameSaveHub.slnx                    ← 13 projets (corrigé)
├── README.md                           ← réécrit : état réel au 8 août
├── BUILD-INTEGRATED-PHASE3.cmd
├── SOURCE-SHA256SUMS.txt
│
├── src/                                ← 13 projets, version Phase 3 0.3.0 r2
├── tests/                              ← Unit (70 cas) + EndToEnd
│
├── deploy/
│   ├── compose.yml
│   ├── compose.portainer.yml           ← aligné sur l'image réellement déployée
│   ├── .env.example
│   ├── traefik/
│   ├── secrets/README.md
│   └── portainer/                      ← NOUVEAU : stacks one-shot d'administration
│       ├── ADMIN-WORLD-LIST.yml
│       ├── ADMIN-CREATE-SHLAGS1.yml
│       ├── ADMIN-IMPORT-SHLAGS1.template.yml
│       └── REVOKE-TEMP-DEVICE.yml
│
├── tools/                              ← 6 scripts (INSTALL/UNINSTALL/STATUS/build/tests NAS/offline)
│
├── docs/
│   ├── ARCHITECTURE.md
│   ├── investigation/                  ← + CROSS-PC-VALIDATION-2026-08.md
│   ├── operations/                     ← + PILOT / ORCHESTRATOR / PHASE3 / CLEANUP / CHECKLIST
│   ├── nas/DEPLOYMENT.md
│   ├── client/PROBE.md
│   ├── deployment/                     ← NOUVEAU : PROTOCOLE-DEPLOIEMENT-PHASE3-0.3.0.md
│   ├── evidence/                       ← NOUVEAU : les 5 rapports .gsh*diag + leur index
│   └── etat-des-lieux-2026-08-08/      ← ce dossier
│
├── artifacts/          (ignoré par Git — sorties de build)
├── snapshots/          (ignoré par Git — preuves de sûreté, à conserver sur disque)
├── diagnostics-output/ (ignoré par Git)
│
└── _archive/           (ignoré par Git — mémoire du projet)
    ├── releases-source/       les 10 archives sources
    ├── releases-binaries/     les 3 ZIP win-x64  (178 Mo)
    ├── build-contexts/        les 2 .tar Portainer
    └── conversations/         codex.md + les 2 liens ChatGPT
```

## Décisions à prendre

Quatre points ne peuvent pas être tranchés à ta place.

### A. Où stocker les 178 Mo de ZIP `win-x64` ?

| Option | Avantage | Inconvénient |
|---|---|---|
| **A1 — `_archive/releases-binaries/`, ignoré par Git** *(recommandé)* | Rien n'est perdu, le dépôt reste léger | 178 Mo restent sur OneDrive |
| A2 — Déplacer hors du dossier projet | Dépôt vraiment propre | Il faut se souvenir où |
| A3 — Supprimer | 178 Mo récupérés | Reconstructibles depuis les sources, mais avec un SDK .NET 10 et du temps |

Ces trois ZIP sont des publications self-contained : leur valeur est surtout historique (« la version exacte que Bob a lancée le 7 août »).

### B. Que faire des 4 documents de planification du 6 août ?

`project.md`, `project-detailed.md`, `plan_detaillé.md`, `tests_et_validation.md`.

Ils ont été générés par une IA à partir du code du 3 août. Ils sont aujourd'hui partiellement faux (voir [04](04-ETAT-DU-DEPOT.md), anomalies 6 et 7) et **entrent en conflit avec la documentation opérationnelle réelle**.

| Option | |
|---|---|
| **B1 — Déplacer dans `_archive/`** *(recommandé)* | Ils sortent du champ de vision sans être détruits |
| B2 — Supprimer | Le présent audit les remplace intégralement |
| B3 — Conserver à la racine | ❌ déconseillé : deux systèmes de numérotation de phases coexisteraient |

### C. Sauvegarde Git : local seul, ou distant ?

| Option | |
|---|---|
| **C1 — Dépôt privé GitHub + `git push`** *(recommandé)* | Vraie sauvegarde hors machine. Le dépôt ne contient aucun secret, c'est vérifié |
| C2 — Git local uniquement | Historique et retour arrière, mais tout reste sur cette machine + OneDrive |

Si C1 : dépôt **privé**. Même sans secrets, le code décrit l'infrastructure réseau.

### D. Est-ce que j'exécute ce rangement ?

Je peux tout faire en une passe, ou seulement l'étape 1 (la plus urgente), ou rien du tout.

---

## Séquence proposée

### Étape 0 — Filet de sécurité (2 min)

Copier l'intégralité du dossier `gamesync` dans une archive datée, **hors du dossier de travail**, avant toute manipulation. Rien de ce qui suit n'est destructeur, mais l'arbre actuel n'a aucun historique Git : il n'y a aucun retour arrière possible.

### Étape 1 — 🔴 Rétablir la source de vérité *(la seule vraiment urgente)*

1. Extraire `GameSaveHub-Integrated-Client-Phase3-0.3.0-r2-source.zip`.
2. Remplacer dans l'arbre de travail : `src/`, `tests/` (sources uniquement), `docs/`, `deploy/`, `tools/`, `GameSaveHub.slnx`, `README.md`, plus `BUILD-INTEGRATED-PHASE3.cmd` et `SOURCE-SHA256SUMS.txt`.
3. Vérifier avec `SOURCE-SHA256SUMS.txt` que l'extraction est intègre.
4. Contrôler que les deux verrous sont bien à `false` après remplacement.
5. Supprimer les `bin/` et `obj/` obsolètes (410 Mo, régénérés au prochain build).

Bénéfice immédiat : l'orchestrateur client, le transformateur de mondes, la vérification joueur et les 6 documents opérationnels **rentrent enfin dans le dépôt**.

⚠️ Le seul fichier de l'arbre actuel plus récent que l'archive est `tools/TEST-NAS-PHASE2-R2-READONLY.ps1` (7 août 16:33) — il n'est pas dans l'archive Phase 3, il faut le conserver.

### Étape 2 — 🔴 Premier commit Git

```bash
git add -A
git commit -m "Import initial : GameSave Hub Integrated Client Phase 3 / 0.3.0 r2"
```

Ajouter au passage un `.gitattributes` (`* text=auto`, plus `*.ps1 text eol=crlf` et `*.cmd text eol=crlf`) afin d'éviter les surprises de fins de ligne sur Windows.

Puis, si option C1 : créer le dépôt privé et pousser.

Optionnellement, reconstituer un historique lisible en committant les jalons dans l'ordre (Phase 0 → Pilot Consolidation → Orchestrator r3 → Phase 3) depuis les archives correspondantes. C'est faisable, chaque archive étant un instantané complet et daté. À ne faire que si tu veux vraiment un historique — sinon un seul commit d'import suffit.

### Étape 3 — 🟠 Ranger les archives

Créer `_archive/` (ajouté au `.gitignore`) et y déplacer :

- `releases-source/` : les 10 ZIP sources ;
- `releases-binaries/` : les 3 ZIP `win-x64` *(selon décision A)* ;
- `build-contexts/` : les 2 `.tar` ;
- `conversations/` : `histo/codex.md`, `GPT_histo1.url`, plus un fichier texte contenant **les deux** liens ChatGPT avec la période que chacun couvre.

### Étape 4 — 🟠 Préserver les preuves

Créer `docs/evidence/` et y déplacer les 5 rapports `.gsh*diag` (≈ 1 Mo au total), avec un `README.md` d'index : machine, date, type de rapport, issue, et ce que chacun démontre.

Ces fichiers sont petits, irremplaçables, et constituent la preuve scientifique du projet : ils méritent d'être **suivis par Git**, contrairement au reste des archives.

`snapshots/` et `artifacts/transfer-20260806-181352/` restent sur disque, ignorés par Git, mais ne doivent jamais être purgés automatiquement.

### Étape 5 — 🟠 Consolider la documentation

1. Déplacer `PROTOCOLE-DEPLOIEMENT-PHASE3-0.3.0.md` (extrait du pack de déploiement) vers `docs/deployment/`.
2. Créer `deploy/portainer/` et y déplacer `REVOKE-TEMP-DEVICE-PORTAINER.yml` ainsi que les trois stacks admin du pack de déploiement.
3. Traiter les 4 documents du 6 août *(décision B)*.
4. Réécrire `README.md` : état réel au 8 août, les deux verrous, le blocage Portainer en cours, et un pointeur vers le présent audit.
5. Corriger `ARTIFACT-FORMAT.md` (ratio 10 → 100) ou durcir le code, au choix.

### Étape 6 — 🟡 Nettoyer

| Fichier | Action |
|---|---|
| `Modelfile` | Supprimer ou déplacer dans `_archive/` — sans rapport avec le projet |
| `bin/` et `obj/` | Supprimer (410 Mo, régénérés au build) |
| `artifacts/probe-win-x64/`, `artifacts/GameSaveHub-Probe-0.1.0-win-x64/`, `artifacts/tools/` | 228 Mo de sorties de build reconstructibles — à supprimer après confirmation que les ZIP correspondants sont archivés |

### Étape 7 — 🟡 Corriger la dette bloquante

Corriger `tools/build-integrated-phase3.ps1` ligne 199 pour exclure `bin/` et `obj/` du contexte Docker, et y intégrer le garde-fou (refus si contexte > 20 Mo ou contenant `bin`/`obj`). Voir [05 — Reste à faire](05-RESTE-A-FAIRE.md), étape 3.

---

## Gain attendu

| | Avant | Après |
|---|---|---|
| Fichiers à la racine | 35 | ~10 |
| Taille du dossier | ~960 Mo | ~30 Mo (+ `_archive/` selon décision A) |
| Sources de vérité | 2 (disque **et** ZIP) | 1 |
| Commits Git | 0 | ≥ 1, poussés |
| Documents de planification concurrents | 5 | 1 |
| Code du 5 au 8 août protégé | ❌ | ✅ |

## Ordre de priorité si tu ne fais qu'une chose

**L'étape 1 puis l'étape 2.** Le reste est du confort ; celles-là arrêtent une perte de données potentielle.
