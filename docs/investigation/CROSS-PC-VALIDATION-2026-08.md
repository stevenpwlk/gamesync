# Validation cross-PC Planet Crafter Game Pass — août 2026

## Statut

Les expériences contrôlées réalisées sur `PC-STEVEN` et `BOBXIME` valident le mécanisme technique d'un **pilote** de transfert de sauvegarde. Elles ne lèvent pas encore le feature gate serveur `GSH_ALLOW_HOST_TRANSFER=false` pour une mise en production générale : le jalon opérationnel exige toujours davantage de cycles reproductibles et de scénarios de reprise.

## Résultats expérimentaux établis

### Portabilité du payload

- un payload `.gshsave` créé sur un compte Xbox peut être injecté dans un nouveau `Standard-X` d'un autre PC ;
- les fichiers `containers.index`, `container.*`, GUID de blobs et racines WGS restent toujours ceux de la machine cible ;
- seul le contenu du blob résolu pour le **nouveau slot local** est remplacé ;
- le `Standard-X` logique d'origine de l'artefact n'a pas besoin d'être le même sur la machine cible ;
- après chargement/sauvegarde, WGS effectue normalement sa rotation de blob et de génération.

### Sélection du joueur local

Le test multijoueur `Shlags1` établit que :

- le joueur local principal est le joueur dont `id == 0` ;
- changer seulement `host=true` sur un autre joueur ne suffit pas ;
- le jeu réaffirme `host=true` sur le joueur ID 0 lors de la sauvegarde ;
- le champ `name` n'est pas une identité portable suffisante : le jeu peut le réécrire avec l'identité locale.

Conséquence : préparer un autre hôte consiste à **échanger les IDs joueur** entre le joueur cible et l'actuel ID 0, puis à rendre le joueur cible unique hôte. Les inventaires, équipements, positions et autres données de chaque personnage restent attachés à leur objet joueur d'origine.

### Garde-fou d'identité

GameSave Hub ne crée pas de personnage de substitution.

Si le pseudo demandé n'existe pas déjà dans l'artefact source, la préparation est refusée. Cela évite qu'un utilisateur ouvre une sauvegarde où il n'a pas de personnage et hérite silencieusement du slot ID 0 d'un autre joueur.

La comparaison du pseudo est Unicode NFC, espaces de bord retirés et insensible à la casse. Si plusieurs joueurs deviennent équivalents après normalisation, la préparation est refusée comme ambiguë.

### Xbox Cloud en ligne

Un nouveau placeholder local a été :

1. créé normalement par Planet Crafter ;
2. synchronisé ;
3. remplacé localement jeu fermé, Internet actif ;
4. surveillé pendant 20 secondes ;
5. chargé, joué et sauvegardé ;
6. observé ensuite comme `Synchronisé` dans Xbox.

Aucun rollback cloud ni conflit visible n'a été observé durant ces essais. Cette preuve valide le parcours pilote en ligne, sans constituer une preuve cryptographique du contenu distant Microsoft.

### Aller-retour réel

Le monde `Shlags1` a suivi le cycle :

`Steven ID0 → Bob ID0 → Steven ID0`

avec conservation des inventaires, équipements, autres joueurs et progression du monde. Le round-trip est donc fonctionnel sur le cas testé.

## Invariants intégrés dans l'adapter

1. Valider entièrement l'artefact `.gshsave` avant toute transformation.
2. Refuser la préparation si le joueur cible n'existe pas exactement une fois.
3. Exiger une topologie saine : IDs joueur uniques, un ID 0, un seul hôte et hôte = ID 0, inventaires/équipements joueur non dupliqués.
4. Préserver les inventaires, équipements, positions et noms lors de l'échange d'IDs.
5. Créer une baseline WGS complète **avant** la création du placeholder d'import.
6. Protéger tous les mondes présents à la baseline par hash logique.
7. Revérifier à l'import que le pseudo attendu existe déjà exactement une fois dans l'artefact et qu'il est déjà ID 0 / unique hôte ; un artefact brut non préparé est refusé.
8. Exiger exactement un nouveau `Standard-X`, d'index supérieur au maximum de la baseline.
9. Exiger que le nouveau placeholder possède un seul joueur ID 0 hôte.
10. Créer un snapshot complet juste avant l'écriture.
11. Résoudre à nouveau le blob physique courant immédiatement avant le remplacement.
12. Refuser si le placeholder ou un monde protégé a changé entre les contrôles.
13. Écrire via fichier temporaire + flush disque + SHA-256.
14. Relire sémantiquement le monde après import.
15. Rollback immédiat depuis le snapshot pré-import si l'écriture a commencé puis échoue.
16. Ne jamais recopier les métadonnées WGS d'un autre PC.

## Ce qui reste hors du pilote automatisé

- lancement automatique du jeu ;
- interprétation automatique d'un dialogue de conflit Xbox Local/Cloud ;
- preuve distante du contenu stocké chez Microsoft ;
- ouverture générale du feature gate serveur.

Ces éléments restent explicitement contrôlés par l'orchestrateur/client et le feature gate opérationnel.
