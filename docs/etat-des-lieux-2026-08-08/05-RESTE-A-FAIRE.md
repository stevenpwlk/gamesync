# 05 — Reste à faire

> ⚠️ **Archive figée au 8-9 août 2026, non mise à jour.** Pour l'état réel actuel du projet, voir [`docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`](../operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md).

> Relevé au **9 août 2026, 11 h**. Chaque affirmation ci-dessous a été vérifiée dans le code, sur le NAS ou sur `PC-STEVEN` au moment de la rédaction — pas reprise d'une version antérieure de ce document.

## Où on en est

Le produit fonctionne de bout en bout. Deux transferts complets ont abouti sur `Shlags1`, avec publication sur le NAS et libération du verrou serveur.

| Élément | État vérifié |
|---|---|
| API | `ghcr.io/stevenpwlk/gamesavehub-api:0.3.1`, `healthy` depuis 9 h |
| Verrou serveur `FeatureGates__AllowHostTransfer` | **ouvert** (`true`) |
| Verrou client `EnableWgsTransfer` sur `PC-STEVEN` | **fermé** (`false`) |
| Sessions serveur | 2, toutes `Completed`, verrous libérés |
| Versions publiées de `Shlags1` | 3, intégrité `storage verify` OK |
| Résidus `pending/` | aucun |
| Tests unitaires | 120, 0 échec |
| Build intégré | vert, 143 fichiers source sous empreinte |

La version courante de `Shlags1` est `fe32692b-c894-4a3f-baa1-add5c9bab87b`, empreinte `84c46254a5b9…`, publiée le 9 août à 10 h 21.

## Ce que le blocage historique est devenu

Le build de l'API dans Portainer, présenté ici comme *le* verrou, **n'est plus un sujet** : la construction passe désormais par GitHub Actions vers GHCR, et le NAS ne fait que tirer une image publiée. La cause probable de l'échec d'origine (`/tmp` en tmpfs de 512 Mo, Portainer natif sans `TMPDIR`) est consignée dans [08-DIAGNOSTIC-NAS](08-DIAGNOSTIC-NAS.md) et n'a plus d'incidence.

Les quatre paliers A à D du protocole de déploiement Phase 3 sont **franchis**.

---

## Ce qu'il reste à faire, dans l'ordre

### 1. Réinstaller le package pilote sur `PC-STEVEN`

Le verrou d'écriture local a été refermé par inadvertance le 9 août à 10 h 29, en installant le package standard par-dessus le pilote. Les deux dossiers portent des noms presque identiques, et la fenêtre d'installation se refermait avant qu'on ait pu lire l'état final.

Corrigé côté outils : l'installeur annonce désormais tout changement d'état du verrou, les fenêtres attendent une touche, et l'application nomme l'installeur à lancer au lieu de se contenter de constater le blocage.

### 2. Essai d'interruption pendant l'import

Jamais réalisé. C'est la preuve n° 5 du go/no-go.

Procédure : `tools/TEST-INTERRUPTION-SERVICE.ps1` en administrateur, puis création du fichier signal `%ProgramData%\GameSaveHub\interrupt.trigger` pendant la phase d'import.

Deux issues sont correctes et dépendent de l'instant exact de la coupure :

- import déjà écrit → la session passe seule en `ReadyToPlay`, **sans réécriture** ;
- placeholder intact → session `Interrupted` / `import_not_written`, **reprise explicite obligatoire**.

C'est la seconde qui démontre qu'aucune écriture dans les sauvegardes ne survient sans action humaine. Un import ne se rejoue jamais tout seul — contrairement à la capture et à la publication, que le service reprend à son démarrage.

### 3. Essai avec redémarrage Windows

Jamais réalisé. Preuve n° 6 du go/no-go, et n° 2 du tableau ci-dessous.

### 4. Séance unique avec Bob

Tout doit être prêt avant de le solliciter : il ne peut pas être rappelé pour un aller-retour. Le package pilote, le code d'invitation et la procédure écrite doivent être réunis, et le rapport de diagnostic généré à la fin de sa séance.

### 5. Deux cycles A → B → A reproductibles supplémentaires

Deux cycles ont abouti, mais tous deux sur `PC-STEVEN` seul. Un cycle au sens du go/no-go implique les deux machines.

### 6. Nettoyage global

Procédure complète dans [NETTOYAGE-APRES-PILOTE](../operations/NETTOYAGE-APRES-PILOTE.md). Ordre impératif : les PC d'abord, le NAS ensuite, le poste de développement en dernier.

Deux mondes homonymes `GSH-SHLAGS-RETURN` (`Standard-3.json` et `Standard-4.json`) sont **conservés délibérément** sur `PC-STEVEN` : ils ont servi à prouver en conditions réelles le correctif de désignation par nom logique, et rien n'oblige à les supprimer avant la fin de la campagne.

---

## Preuves manquantes avant ouverture générale

| # | Preuve requise | État au 9 août |
|---|---|---|
| 1 | Deux cycles A → B → A supplémentaires reproductibles | ❌ 2 cycles réussis, mais sur une seule machine |
| 2 | Scénario incluant un redémarrage Windows | ❌ jamais testé |
| 3 | Stratégie documentée face à un vrai dialogue Local/Cloud | ❌ jamais rencontré, donc jamais documenté |
| 4 | Intégration client/service de bout en bout avec rollback | ✅ deux transferts complets, dont un repris après échec |
| 5 | Revue des logs et du comportement de reprise après interruption | ⚠️ reprise après échec de capture prouvée ; coupure pendant l'import à faire |

Les huit conditions historiques du [GO-NOGO](../operations/GO-NOGO.md) restent la référence. **Un échec sur cette liste impose le no-go documenté** — c'était la règle posée au premier jour, elle n'a pas changé parce que le produit marche.

---

## Défauts trouvés en exécution réelle

Aucun de ces cinq défauts n'était détectable autrement qu'en faisant tourner le produit. Ils sont consignés parce qu'ils disent quelque chose sur la nature des tests qui manquaient.

| Défaut | Symptôme | Correctif |
|---|---|---|
| Contrôle du SID avant lecture du canal | Aucun bouton ne fonctionnait | Lire la première ligne avant `ImpersonateNamedPipeClient` |
| Assemblies d'identité non chargées en exe mono-fichier | `FileNotFoundException: System.Security.Claims` sous usurpation | Préchargement explicite avant toute usurpation |
| Capture désignée par nom affiché | `Sequence contains more than one matching element` avec deux mondes homonymes | Désignation par nom logique, refus explicite si ambiguïté |
| Chunks d'upload jamais supprimés | `pending/` croissait indéfiniment | Nettoyage après validation de la transaction |
| Changement de verrou silencieux | Transfert bloqué sans explication | Annonce explicite, fenêtre qui attend, message actionnable |

---

## Dette technique

Les quatre points listés dans la version précédente de ce document sont **résolus** et vérifiés : `GameSaveHub.slnx` contient `Client.Orchestration` ; le contexte Docker exclut `bin`/`obj` et est plafonné à 20 Mo ; `deploy/compose.portainer.yml` est sur `0.3.1` ; le ratio de compression documenté (100) correspond au code.

| Sujet | Détail |
|---|---|
| `WorldSessionStateMachine` ne garde rien | `CanTransition` et `EnsureTransition` ne sont appelés **nulle part** en production ; seul `CanUserAbort` l'est. Les vraies préconditions sont écrites endpoint par endpoint dans `Program.cs`, et `HoldsWorldLock` est court-circuité par `ReleasedAtUtc == null`. Dix tests unitaires valident donc des règles qui ne gardent rien, et la lecture de la classe induit en erreur : elle interdit `Interrupted → InGame`, alors que `/import-starting` l'autorise explicitement (ligne 376) — c'est justement ce qui rend une reprise possible après un redémarrage. À trancher **après** la campagne : soit faire passer les endpoints par la machine d'états, soit la supprimer |
| Installateur Windows non signé | Avertissement SmartScreen — connu et assumé depuis la conception |
| Numérotation des phases | Deux systèmes concurrents (voir [04](04-ETAT-DU-DEPOT.md), anomalie 6) |
| `deploy/compose.portainer.yml` | Le dépôt conserve `AllowHostTransfer: "false"` alors que la stack en service est ouverte : divergence volontaire, mais à refermer en fin de campagne |

## Ce qui n'a jamais été commencé

Tout est postérieur à la campagne pilote :

- mise à jour client par manifeste ECDSA signé + vérification SHA-256 ;
- pilote à 4 joueurs et import du monde principal (aujourd'hui : 2 joueurs, monde jetable) ;
- SDK d'adaptateurs et chargement dynamique, explicitement reporté après stabilisation de Planet Crafter ;
- tests opérationnels de restauration : reconstruction après perte de SQLite, restauration d'archive validée sur dossier isolé.
