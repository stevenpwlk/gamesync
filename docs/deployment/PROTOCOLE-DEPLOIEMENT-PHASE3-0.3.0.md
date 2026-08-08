# GameSave Hub — Déploiement Phase 3 / API 0.3.0

## État validé avant déploiement

- Integrated Client Phase 3 / 0.3.0 r2 : 70/70 tests.
- `FeatureGates__AllowHostTransfer=false`.
- `ClientService:EnableWgsTransfer=false`.
- aucune nouvelle migration SQLite.
- artefact canonique Shlags1 :
  `SHLAGS1-CANONICAL-ROUNDTRIP-20260807.gshsave`
- SHA-256 artefact :
  `30af9efca4bed6b7042c7dae4f83fedaa8fc9311c22153735d3a00fc96d76495`
- joueurs :
  - Stevenpwlk — ID 0 — hôte — inventory 3 — equipment 4
  - Maxdrake59 — ID 4 — non-hôte — inventory 7 — equipment 8
  - BoB XiMe — ID 7 — non-hôte — inventory 5 — equipment 6

## Palier A — API NAS 0.3.0

1. Dans Portainer > Images > Build a new image :
   - Name : `gamesavehub-api:0.3.0`
   - Build context : le fichier produit localement par le build Phase 3 :
     `artifacts\GameSaveHub-API-0.3.0-Portainer-Build-Context.tar`
   - Dockerfile path :
     `src/GameSaveHub.Server.Api/Dockerfile`
2. Attendre la fin complète du build.
3. Vérifier que `gamesavehub-api:0.2.0` ET `gamesavehub-api:0.3.0` existent.
   Ne pas supprimer 0.2.0 : rollback.
4. Avant la mise à jour de stack :
   - arrêter uniquement `gamesavehub-api-1`;
   - avec TOS File Manager copier tout `/Volume2/gamesavehub/data/` vers
     `/Volume2/gamesavehub/backups/pre-api-0.3.0-20260808/`;
   - redémarrer l'API si la mise à jour n'est pas faite immédiatement.
5. Portainer > Stacks > `gamesavehub` > Editor :
   remplacer uniquement :
   `image: gamesavehub-api:0.2.0`
   par :
   `image: gamesavehub-api:0.3.0`
6. Vérifier impérativement :
   `FeatureGates__AllowHostTransfer: "false"`
7. Update the stack.
8. Attendre `gamesavehub-api-1 = healthy`.
9. Contrôler les logs :
   - pas de crash loop;
   - pas de message de migrations SQLite en attente;
   - pas d'erreur d'accès `/data`.

Rollback : remettre uniquement `gamesavehub-api:0.2.0` et Update stack.
Aucune migration Phase 3 n'étant ajoutée, le rollback image est prévu pour être direct.

## Palier B — Validation API 0.3.0 en lecture seule

Depuis Windows :

`powershell -ExecutionPolicy Bypass -File .\TEST-NAS-PHASE3-READONLY.ps1`

Attendu :
- DNS OK;
- TCP 18443 OK;
- `/healthz` = 200 / Healthy;
- `/api/v1/worlds` sans JWT = 401;
- `/api/v1/worlds/<GUID>/preview` sans JWT = 401.

Ce test n'écrit ni dans SQLite ni dans WGS.

## Palier C — Préparation de Shlags1 sur le NAS

### 1. Déposer l'artefact

Avec TOS File Manager, créer si besoin :

`/Volume2/gamesavehub/imports/`

Puis y déposer :

`SHLAGS1-CANONICAL-ROUNDTRIP-20260807.gshsave`

Ne pas le déposer dans `/data/objects` manuellement.

### 2. Lister les mondes déjà présents

Déployer temporairement `ADMIN-WORLD-LIST-PORTAINER.yml`.

Lire ses logs puis supprimer la stack.

Ne pas créer de nouveau `Shlags1` tant que cette sortie n'a pas été vérifiée.

### 3. Si aucun monde Shlags1 n'existe

Déployer `ADMIN-CREATE-SHLAGS1-PORTAINER.yml`.

Les logs doivent fournir :

`<WORLD_GUID>    Shlags1`

Conserver ce GUID.

Supprimer ensuite cette stack temporaire.

### 4. Import initial

Modifier `ADMIN-IMPORT-SHLAGS1-PORTAINER.template.yml` :

remplacer :
`REPLACE_WITH_SHLAGS1_WORLD_GUID`

par le GUID obtenu à l'étape précédente.

Déployer la stack.

L'admin :
- valide l'enveloppe `.gshsave`;
- calcule son SHA-256;
- publie l'objet dans le stockage immuable;
- crée une SaveVersion;
- définit cette version comme version courante de Shlags1.

Attendu dans les logs :
`<VERSION_GUID>    <SHA256>    <LENGTH>`

Le SHA-256 attendu est :
`30af9efca4bed6b7042c7dae4f83fedaa8fc9311c22153735d3a00fc96d76495`

Supprimer la stack d'import après succès.

### 5. Vérification finale admin

Relancer `ADMIN-WORLD-LIST-PORTAINER.yml`.

Shlags1 doit maintenant afficher un `CurrentVersionId` et non `aucune version`.

## Palier D — Vrai client Windows

Ne faire ce palier qu'après validation de Shlags1 sur le NAS.

Le ZIP généré par le build local est :

`artifacts\GameSaveHub-Client-Phase3-0.3.0-win-x64.zip`

1. Extraire le ZIP dans un dossier temporaire.
2. Ouvrir PowerShell **en administrateur** dans ce dossier.
3. Lancer :
   `.\INSTALL-GAMESAVEHUB-CLIENT.ps1`
4. Attendu :
   - utilisateur interactif = le joueur Windows;
   - SID résolu;
   - bon `AppData\Local`;
   - service `GameSaveHubClient` = Running;
   - `EnableWgsTransfer=false`.
5. Ne pas activer le transfert.
6. Créer ensuite une invitation temporaire via l'admin NAS.
7. Ouvrir l'application `GameSave Hub`.
8. Renseigner :
   - DeviceName : nom du PC;
   - pseudo Planet Crafter : `Stevenpwlk`;
   - code d'invitation.
9. Associer.
10. Charger les mondes.
11. Sélectionner `Shlags1`.
12. Ouvrir l'aperçu et lancer le préflight.

Attendu :
- trois joueurs visibles;
- `Stevenpwlk` présent exactement une fois;
- statut `preflight_ready`;
- bouton de transfert désactivé car `EnableWgsTransfer=false`.

Test négatif ensuite :
- modifier temporairement le pseudo local vers un pseudo inexistant;
- refaire le préflight;
- attendu : `player_not_found`;
- remettre `Stevenpwlk`.

Aucune écriture WGS ne doit avoir lieu pendant cette Phase 3.
