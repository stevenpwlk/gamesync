# Runbook pilote — slot local permanent (Lot 2, `0.4.0-pilot`)

**Statut :** procédure prête, **non exécutée**. Chaque étape marquée « Porte d'approbation » exige un accord explicite avant toute écriture réelle (WGS ou NAS). Ne rien exécuter au-delà d'une porte sans cet accord.

**Périmètre :** installation/migration du PC de Steven (Tâche 12), résilience et accessibilité (Tâche 13), puis validation avec Bob (Tâche 14). Voir la [checklist de validation](CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md) pour l'état réel courant et [`LOT2-CONTEXTUAL-CLIENT.md`](LOT2-CONTEXTUAL-CLIENT.md) pour le comportement attendu.

## 0. Préflight (lecture seule, sans porte)

- Serveur sain : `home-context` renvoie `serverHealthy=true`.
- Monde principal `Available`, aucune session serveur ni locale active.
- Jeu fermé, WGS stable (`STATUS-GAMESAVEHUB-CLIENT.ps1` ou `home-context`).
- `git status` propre sur `codex/v1-lot2-contextual-app`, build et tests locaux verts (`dotnet test GameSaveHub.slnx`).
- Paquet `0.4.0-pilot` reconstruit et son SHA-256 noté : `GameSaveHub-Client-Lot2-0.4.0-PILOTE-win-x64.zip`.

## 1. Snapshot avant toute migration

**Porte d'approbation : accord explicite avant snapshot WGS et sauvegarde ProgramData.**

- Snapshot WGS complet (`snapshot --output snapshots --test-world <monde jetable> --acknowledge-test-world`, ou l'équivalent lecture seule si aucun monde jetable n'est disponible).
- Copie de `%ProgramData%\GameSaveHub` (contient la clé CNG, `client-state.json`, les sessions).
- Noter le nombre de fichiers WGS et le hash du manifeste de snapshot, comme lors du nettoyage du 9 août.

Sans ces deux preuves, ne pas passer à l'étape 2.

## 2. Installation `0.4.0-pilot`

**Porte d'approbation : accord explicite avant élévation UAC et installation.**

- Lancer `INSTALLER-GAMESAVEHUB-PILOTE.cmd` en administrateur (élévation UAC attendue — prévenir avant de cliquer « Oui »).
- Vérifier après installation :
  - Service `GameSaveHubClient` à l'état `Running`, démarrage `Automatic (Delayed Start)`.
  - `STATUS-GAMESAVEHUB-CLIENT.ps1` répond en moins de 30 secondes.
  - Le device ID, le pseudo enregistré et la clé CNG existante sont préservés (pas de ré-enrôlement).
  - `Slot local permanent : non configuré` — attendu, puisque le rattachement n'a pas encore eu lieu.

Ne pas encore prendre la main.

## 3. Rattachement explicite de `Standard-5.json`

**Porte d'approbation : accord explicite avant l'action de rattachement (elle n'écrit que `managed-slot.json`, jamais WGS).**

- Sur le PC de Steven, le seul candidat historique pertinent est `Standard-5.json`, actuellement affiché `GSH-SHLAGS-RETURN` (état confirmé lors du nettoyage du 9 août — `Shlags1` et `Standard-1` restent des mondes distincts et protégés).
- L'accueil doit afficher l'état « Un monde partagé existant a été trouvé » avec l'action `Rattacher ce monde`. Le nom logique n'apparaît jamais dans l'IHM ; il reste uniquement dans les diagnostics.
- Après confirmation, vérifier :
  - `managed-slot.json` existe et pointe vers `Standard-5.json` (vérification par diagnostic, pas par capture d'écran contenant le nom logique).
  - Aucun octet WGS n'a changé (comparer les hash de `Standard-5.json` avant/après).
  - L'accueil repasse à `Le monde est prêt` / `Prendre la main`.

## 4. Première réutilisation (premier import sûr → renommage en `GSH-MONDE-PARTAGE`)

**Porte d'approbation : accord explicite avant cette première écriture WGS réelle du slot permanent.**

- Prendre la main depuis l'accueil.
- Vérifier qu'aucun nouveau monde logique n'apparaît (pas de `Standard-6.json`).
- Lancer Xbox, charger `GSH-MONDE-PARTAGE` (c'est désormais l'unique nom visible pour ce slot), faire une modification identifiable, sauvegarder, fermer complètement.
- Vérifier : capture réussie, version publiée, monde serveur de nouveau `Available`, `Standard-5.json` reste le seul nom logique concerné.

## 5. Deuxième réutilisation

**Porte d'approbation : accord explicite, même si le mécanisme est désormais identique à l'étape 4.**

- Répéter la prise en main. Vérifier que le nom logique lié (`Standard-5.json`) est identique aux deux réutilisations, qu'aucun nouveau monde n'a été créé, et que la progression (inventaire, position) suit correctement.

## 6. Résilience

- Redémarrage Windows en pleine session `InGame` : après connexion, la session doit reprendre sans nouvel import, capturer normalement à la fermeture du jeu.
- Interruption contrôlée pendant l'import : au redémarrage du service, la réconciliation doit identifier soit le contenu précédent soit le contenu importé, ne jamais créer un second slot, et ne retenter l'écriture que sur reprise explicite.
- `maintenance-status` doit revenir à `SafeToUpdate=true` en moins de 30 secondes une fois le jeu fermé et la session terminée.

## 7. Vérifications serveur et nettoyage

- Confirmer qu'aucune session serveur ni locale ne reste active après chaque test.
- Confirmer qu'aucune suppression automatique n'a eu lieu (`Shlags1` et `Standard-1` inchangés).
- Journaliser chaque preuve (hash, nombre de fichiers, capture du diagnostic — jamais une capture d'écran contenant un nom logique ou un chemin `C:\Users\...`) dans la checklist de validation.

## 8. Rollback

- En cas d'échec avant l'étape 4 : réinstaller la version précédente (celle sans `managed-slot.json`) est possible, `ProgramData` n'a pas encore été modifié de façon incompatible.
- En cas d'échec après l'étape 4 (contenu du slot déjà remplacé au moins une fois) : ne pas revenir à une version antérieure au socle `0.4.0-pilot`. Restaurer depuis le snapshot WGS de l'étape 1 si nécessaire, avec accord explicite avant toute restauration réelle.

## Étape suivante : Bob (Tâche 14)

Ne commencer qu'après que les étapes 1 à 7 sont cochées avec preuves dans la checklist. Bob reçoit exactement le même ZIP `0.4.0-pilot` (même SHA-256), réalise sa propre configuration initiale (pas de rattachement, son PC n'a jamais eu de slot), puis le cycle réel `Steven → Bob → Steven` est exécuté avec vérification de topologie, inventaire et versions à chaque relais.

Ce que Steven doit préparer et envoyer à Bob, et le guide pas-à-pas à lui transmettre : [`GUIDE-BOB-CONFIGURATION-INITIALE.md`](GUIDE-BOB-CONFIGURATION-INITIALE.md).
