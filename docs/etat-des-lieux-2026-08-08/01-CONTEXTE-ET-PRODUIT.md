# 01 — Contexte et produit

> ⚠️ **Archive figée au 8-9 août 2026, non mise à jour.** Pour l'état réel actuel du projet, voir [`docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`](../operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md).

## L'objectif réel

Permettre à **quatre joueurs, sur quatre PC Windows 11 différents, avec quatre comptes Xbox différents**, de partager **un seul monde multijoueur** de *The Planet Crafter* (version PC Game Pass) :

- chacun peut devenir hôte à son tour ;
- chacun retrouve exactement le point où les autres se sont arrêtés ;
- son propre personnage, son inventaire, son équipement et sa position sont conservés ;
- aucune sauvegarde personnelle existante n'est jamais écrasée.

Le partage transite par un serveur auto-hébergé sur le NAS TerraMaster (`192.168.1.73`), exposé sur Internet en `https://saves.stevenpwlk.fr:18443`.

Périmètre V1 verrouillé dès la conception : Windows 11 x64 uniquement, un joueur Xbox par PC, un monde, interface en français, *The Planet Crafter* PC Game Pass uniquement. Pas de Windows 10, pas d'ARM64, pas de Steam, pas de console.

## Pourquoi c'est difficile

Le jeu, en version Game Pass, stocke ses sauvegardes dans **WGS** (Windows Game Saves / Xbox Connected Storage). Microsoft y applique sa propre synchronisation cloud et ses propres verrous. Toute écriture « par derrière » entre donc en concurrence avec Gaming Services.

Cette contrainte a structuré tout le projet dès le premier jour : la faisabilité du transfert d'hôte a été traitée comme un **jalon bloquant à prouver expérimentalement**, jamais comme une tâche à implémenter. La décision initiale était explicite : en cas d'échec, de conflit cloud non maîtrisé ou de perte d'identité/inventaire, **arrêter le projet au diagnostic et documenter le no-go** — sans livrer de mode dégradé.

## Les découvertes qui définissent le produit

Ces trois résultats viennent d'expériences réelles sur deux PC (voir [03 — Historique](03-HISTORIQUE.md)), pas de suppositions.

### 1. Le joueur local est l'ID 0

Le joueur incarné localement est celui dont `id == 0` dans la sauvegarde sérialisée. Mettre `host=true` sur un autre joueur **ne suffit pas** : le jeu réaffirme `host=true` sur le joueur ID 0 à la sauvegarde suivante. Le champ `name` n'est pas non plus une identité portable — le jeu peut le réécrire avec l'identité Xbox locale.

**Conséquence produit :** préparer un autre hôte = **échanger les IDs joueur** entre le joueur cible et l'actuel ID 0, puis rendre le joueur cible unique hôte. Les inventaires, équipements, positions et autres données restent attachés à leur objet joueur d'origine.

### 2. Le payload est portable, les métadonnées WGS ne le sont pas

Un payload `.gshsave` créé sur un compte Xbox peut être injecté dans un **nouveau** `Standard-X` d'un autre PC. En revanche `containers.index`, `container.*`, les GUID de blobs et les racines WGS restent **toujours** ceux de la machine cible. Seul le contenu du blob résolu pour le nouveau slot local est remplacé.

### 3. Xbox Cloud tolère l'opération

Un placeholder créé normalement par le jeu, synchronisé, puis remplacé localement jeu fermé et Internet actif, a été rechargé, joué, sauvegardé, puis observé comme `Synchronisé` dans l'application Xbox. Aucun rollback cloud ni conflit visible sur les essais réalisés.

C'est une preuve **empirique** du parcours en ligne, pas une preuve cryptographique du contenu distant stocké chez Microsoft. Cette nuance est maintenue partout dans la documentation du projet.

## Les règles de sûreté non négociables

Elles sont implémentées dans le code, pas seulement documentées.

### Garde-fou anti-substitution

**GameSave Hub ne crée jamais de personnage de substitution.** Si le pseudo demandé n'existe pas déjà dans l'artefact source, la préparation est refusée. Cela évite qu'un utilisateur ouvre une sauvegarde où il n'a pas de personnage et hérite silencieusement du slot ID 0 d'un autre joueur.

La comparaison de pseudo est **Unicode NFC, espaces de bord retirés, insensible à la casse**. Zéro correspondance → `player_not_found`. Plusieurs correspondances → `player_ambiguous`. Exactement une → préflight compatible.

### Les 16 invariants de l'import

1. Valider entièrement l'artefact `.gshsave` avant toute transformation.
2. Refuser la préparation si le joueur cible n'existe pas exactement une fois.
3. Exiger une topologie saine : IDs joueur uniques, un ID 0, un seul hôte, hôte = ID 0, inventaires/équipements non dupliqués.
4. Préserver inventaires, équipements, positions et noms lors de l'échange d'IDs.
5. Créer une baseline WGS complète **avant** la création du placeholder d'import.
6. Protéger tous les mondes présents à la baseline par hash logique.
7. Revérifier à l'import que le pseudo attendu est déjà ID 0 / unique hôte ; un artefact brut non préparé est refusé.
8. Exiger exactement un nouveau `Standard-X`, d'index supérieur au maximum de la baseline.
9. Exiger que le nouveau placeholder ait un seul joueur ID 0 hôte.
10. Créer un snapshot complet juste avant l'écriture.
11. Résoudre à nouveau le blob physique courant immédiatement avant le remplacement.
12. Refuser si le placeholder ou un monde protégé a changé entre les contrôles.
13. Écrire via fichier temporaire + `WriteThrough` + flush disque + SHA-256.
14. Relire sémantiquement le monde après import.
15. Rollback immédiat depuis le snapshot pré-import si l'écriture a commencé puis échoué.
16. Ne jamais recopier les métadonnées WGS d'un autre PC.

### Le double verrou de production

Deux verrous **indépendants** interdisent aujourd'hui tout transfert réel :

| Verrou | Emplacement | Valeur |
|---|---|---|
| `FeatureGates__AllowHostTransfer` | NAS — `deploy/compose.portainer.yml`, `appsettings.json` | `false` |
| `ClientService:EnableWgsTransfer` | PC — `src/GameSaveHub.Client.Service/appsettings.json` | `false` |

Le script de build Phase 3 vérifie ces deux valeurs (`Test-Phase3Guards`) et refuse de compiler si elles ont été modifiées.

L'existence des commandes pilote ne signifie **pas** que le transfert d'hôte est ouvert. Les conditions d'ouverture sont listées dans [05 — Reste à faire](05-RESTE-A-FAIRE.md).

## Ce qui reste explicitement hors du pilote automatisé

- lancement automatique du jeu (`CanLaunchGame=false`) ;
- interprétation automatique d'un dialogue de conflit Xbox Local/Cloud ;
- preuve distante du contenu stocké chez Microsoft ;
- ouverture générale du feature gate serveur.

## Acteurs et machines

| Nom | Rôle |
|---|---|
| `PC-STEVEN` (`192.168.1.64`) | PC de référence, joueur `Stevenpwlk` |
| `BOBXIME` | PC distant de l'ami, utilisateur Windows `maxim`, joueur `BoB XiMe` |
| NAS TerraMaster (`192.168.1.73`) | Serveur Docker/Portainer, volume `/Volume2/gamesavehub` |
| `saves.stevenpwlk.fr:18443` | Point d'entrée public (Traefik) |

Joueurs présents dans l'artefact canonique `Shlags1` :

| Pseudo | ID | Hôte | Inventaire | Équipement |
|---|---|---|---|---|
| `Stevenpwlk` | 0 | oui | 3 | 4 |
| `Maxdrake59` | 4 | non | 7 | 8 |
| `BoB XiMe` | 7 | non | 5 | 6 |
