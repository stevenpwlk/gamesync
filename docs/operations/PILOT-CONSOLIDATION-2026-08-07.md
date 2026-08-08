# Consolidation pilote — transfert d'hôte Planet Crafter

Date : 7 août 2026

## Objectif

Transformer les résultats des expériences cross-PC en invariants produit réutilisables, sans ouvrir encore le feature gate serveur à la production générale.

## Fonctionnalités intégrées

### Préparation d'hôte

`PlanetCrafterGamePassAdapter.PrepareForHostAsync(...)` :

- valide l'artefact `.gshsave` avant toute transformation ;
- exige un pseudo cible non vide ;
- compare les pseudos en Unicode NFC, espaces de bord retirés, sans tenir compte de la casse ;
- refuse si le pseudo n'existe pas ;
- refuse si plusieurs joueurs correspondent ;
- exige une topologie saine : IDs joueur uniques, inventaires et équipements joueur non dupliqués, exactement un ID 0 et exactement un hôte, avec hôte = ID 0 ;
- échange uniquement les IDs du joueur cible et de l'actuel ID 0 ;
- rend le joueur cible ID 0 / unique hôte ;
- conserve noms, positions, inventaires, équipements et autres données de personnage ;
- produit un nouvel artefact `.gshsave` validé.

### Garde-fou anti-substitution

L'import exige également le pseudo attendu.

Un artefact brut où ce joueur n'est pas déjà ID 0 / unique hôte est refusé. Il est donc impossible de contourner `prepare-host` en important directement une sauvegarde où le joueur n'existe pas ou n'est pas le slot local préparé.

### Baseline d'import

`CreateImportBaselineAsync(...)` crée une capture complète WGS avant la création du placeholder et mémorise :

- tous les fichiers et leurs SHA-256 ;
- tous les mondes existants et leur hash logique ;
- le plus grand index `Standard-X` existant.

### Import ciblé

`ImportPortableArtifactAsync(...)` :

- valide l'artefact et le joueur attendu ;
- valide l'intégrité de la baseline ;
- vérifie que tous les mondes protégés sont inchangés ;
- exige exactement un nouveau `Standard-X` ;
- exige que son index soit supérieur au maximum de la baseline ;
- accepte les caractères invisibles uniquement en bordure du nom affiché du placeholder ;
- exige un placeholder neuf avec un seul joueur ID 0 / hôte ;
- crée un snapshot complet juste avant écriture ;
- revérifie tous les hashes et le blob physique immédiatement avant remplacement ;
- écrit via fichier temporaire + `WriteThrough` + flush disque ;
- vérifie le SHA-256 final ;
- relit le monde sémantiquement ;
- restaure automatiquement le placeholder depuis le snapshot pré-import si une erreur survient après le début de l'écriture.

Aucune métadonnée `containers.index`, `container.*` ou GUID WGS n'est transférée depuis un autre PC.

## CLI pilote

Le projet `GameSaveHub.Diagnostics` expose :

```text
prepare-host
import-baseline
import-artifact
```

`import-artifact` exige `--acknowledge-pilot-import` et le pseudo attendu.

## Service Windows

Le fallback dangereux sur le SID du processus service a été supprimé. `ClientService:RegisteredUserSid` est désormais réellement obligatoire. Cela évite qu'un service LocalSystem configure par erreur le named pipe pour son propre SID.

## Feature gate

Les capacités techniques `CanPrepareForHost` et `CanImportPortableArtifact` sont maintenant actives dans l'adapter avec le statut :

`pilot-validated-production-gate-required`

Le serveur doit néanmoins conserver :

`GSH_ALLOW_HOST_TRANSFER=false`

jusqu'à validation du jalon opérationnel complet.

## Tests ajoutés

Les nouveaux tests couvrent notamment :

- pseudo absent ;
- pseudo ambigu après normalisation ;
- permutation ID 0 / joueur cible ;
- conservation inventaire/équipement/position ;
- joueur déjà ID 0 ;
- hôte incohérent ;
- import direct d'un artefact non préparé ;
- monde protégé modifié après baseline ;
- plusieurs nouveaux mondes ;
- placeholder avec caractère invisible de bord ;
- import en présence d'une route réseau active ;
- préservation du monde source lors de l'import dans le nouveau slot.

Nombre attendu de cas unitaires : 43.

## Étape suivante

Après compilation réelle et réussite des tests sur Windows :

1. conserver le feature gate serveur fermé ;
2. intégrer un orchestrateur de transfert dans le client avec état persistant/reprise ;
3. résoudre explicitement le contexte utilisateur Windows pour l'accès WGS depuis le service ;
4. implémenter la surveillance post-import / Xbox Cloud et les états d'incident ;
5. brancher ensuite l'acquisition/commit serveur sur cet orchestrateur ;
6. réaliser les cycles reproductibles restants avant ouverture générale.

## Révision r2

Correction de compilation CA1859 dans `PlanetCrafterWorldTransformer`: le premier paramètre de `PlayersEquivalentForTopology` est typé `DiscoveredPlayer[]`, conformément au type réellement produit par l’appelant. Aucun changement fonctionnel de la préparation/import.
