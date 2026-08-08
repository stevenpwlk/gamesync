# GameSave Hub — Integrated Client Phase 3 / 0.3.0

## But

Cette phase transforme les composants validés séparément en une première installation réelle du client.

Elle reste volontairement en **mode préflight** :

- authentification persistante : oui ;
- catalogue des mondes NAS : oui ;
- lecture du manifeste courant : oui ;
- vérification du pseudo joueur : oui ;
- service Windows : oui ;
- interface WPF : oui ;
- écriture WGS : **non** ;
- feature gate serveur de transfert : **non**.

Deux verrous indépendants restent fermés :

1. `FeatureGates__AllowHostTransfer=false` sur le NAS ;
2. `ClientService:EnableWgsTransfer=false` sur le PC.

## Règle joueur

Un PC possède un pseudo Planet Crafter configuré localement.

Avant tout futur transfert, le serveur renvoie la liste des joueurs sérialisés dans la version courante du monde.

Le client exige exactement une correspondance, avec trim + Unicode Form C + comparaison insensible à la casse.

- aucune correspondance → `player_not_found` ;
- plusieurs correspondances → `player_ambiguous` ;
- une seule correspondance → préflight compatible.

La sélection du joueur n'est donc plus un champ libre au moment de l'import.

## API 0.3.0

Nouvelles routes authentifiées et en lecture seule :

- `GET /api/v1/worlds`
- `GET /api/v1/worlds/{id}/preview`

Le preview ne renvoie pas le payload de la sauvegarde. Il renvoie seulement les métadonnées nécessaires à l'UX et au garde-fou joueur :

- nom du monde ;
- version courante ;
- hash d'artefact ;
- nom d'affichage de la sauvegarde ;
- seed ;
- joueurs : ID, pseudo, host, IDs inventaire/équipement.

L'artefact immuable est validé et son payload est rehashé avant de produire le preview.

Aucune migration SQLite n'est ajoutée.

## Identité PC persistante

Le service utilise une clé ECDSA P-256 CNG :

- provider Microsoft Software KSP ;
- MachineKey ;
- usage signature ;
- `CngExportPolicies.None`.

La clé privée n'est jamais envoyée au serveur.

Le DeviceId et le pseudo local sont conservés dans `%ProgramData%\GameSaveHub\client-state.json`.

## Installation Windows

Le package publié contient :

- `Service/`
- `App/`
- `INSTALL-GAMESAVEHUB-CLIENT.ps1`
- `UNINSTALL-GAMESAVEHUB-CLIENT.ps1`
- `STATUS-GAMESAVEHUB-CLIENT.ps1`

L'installateur :

1. doit être lancé administrateur ;
2. résout l'utilisateur Windows interactif et son SID ;
3. résout son profil `AppData\Local`;
4. installe le service sous LocalSystem ;
5. configure le pipe uniquement pour ce SID ;
6. injecte le bon profil utilisateur dans l'adapter ;
7. impose `EnableWgsTransfer=false`;
8. installe l'application WPF et un raccourci menu Démarrer.

## Prochaine validation après compilation

Après 70/70 tests et publication :

1. mise à jour du NAS vers API 0.3.0, feature gate toujours fermé ;
2. création d'un monde serveur pilote `Shlags1` et import initial d'un `.gshsave` validé ;
3. installation du client Phase 3 sur PC-STEVEN ;
4. enrôlement persistant avec une nouvelle invitation ;
5. affichage de `Shlags1` depuis le catalogue ;
6. preview des trois joueurs ;
7. pseudo configuré `Stevenpwlk` → `preflight_ready`;
8. pseudo absent volontaire → refus ;
9. confirmation qu'aucune écriture WGS n'a été réalisée.

La Phase 4 pourra ensuite ouvrir localement et côté serveur le transfert uniquement pour le monde pilote.
