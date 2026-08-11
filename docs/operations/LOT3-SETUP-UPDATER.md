# Lot 3 — Installateur unique et mises à jour à distance

**Statut à la date de cette page : implémentation automatisée terminée (Tâches 1 à 13 du plan), rien de tout cela n'a encore tourné sur un vrai PC Windows.** Voir [`LOT3-VALIDATION-CHECKLIST.md`](LOT3-VALIDATION-CHECKLIST.md) pour ce qui reste à valider réellement avant toute diffusion.

Pour la conception complète (décisions produit, formats, tables d'état) : [`docs/superpowers/specs/2026-08-11-lot3-updater-design.md`](../superpowers/specs/2026-08-11-lot3-updater-design.md). Pour le détail tâche par tâche de ce qui a été construit : [`docs/superpowers/plans/2026-08-11-lot3-updater-implementation.md`](../superpowers/plans/2026-08-11-lot3-updater-implementation.md).

## En une phrase

Le Lot 2 livrait un client fonctionnel mais dont l'installation et la mise à jour restaient manuelles (zip, script `.cmd`, renvoi individuel à chaque joueur). Le Lot 3 remplace tout cela par un seul exécutable signé, `GameSaveHub-Setup.exe`, qui sert à la fois d'installateur, de moteur de mise à jour silencieuse et de désinstallateur — sans toucher au protocole de transfert de sauvegarde, au slot permanent ou au verrou de version déjà livrés au Lot 2.

## Les trois modes de `GameSaveHub-Setup.exe`

| Mode | Déclenchement | Résumé |
|---|---|---|
| `--install` (par défaut) | Double-clic joueur | Élévation UAC, installation du service `GameSaveHubClient` + app WPF + raccourci, préservation de l'identité CNG et du pseudo si déjà présents, suppression de l'ancienne inscription du service avant recréation (permet la réinstallation/mise à niveau), puis enregistrement d'une tâche planifiée `GameSaveHubUpdater` (droits élevés, sans session ouverte, toutes les 6 h, invoque `--auto-update`). |
| `--auto-update` | Tâche planifiée `GameSaveHubUpdater` | Silencieux, aucune fenêtre. Interroge `GET /api/v1/client/latest`, vérifie la signature du manifeste puis le SHA-256 du paquet avant tout téléchargement définitif, interroge `maintenance-status` par tube nommé et **s'arrête sans rien modifier** si `SafeToUpdate=false`, puis applique la bascule de dossier (§ci-dessous) et vérifie que le service répond sous 30 s. |
| `--uninstall` | Ligne de commande ou bouton « Désinstaller » | Interroge `maintenance-status` en premier (refuse si session active ou transition en cours), appelle `POST /api/v1/device/revoke-self` en best-effort (échec = suppression locale quand même, avec message clair), puis supprime service, app, raccourci, tâche planifiée et **tout `%ProgramData%\GameSaveHub`** (identité CNG, `managed-slot.json`, `client-state.json`, `update-staging`) — contrairement à l'ancien `UNINSTALL-GAMESAVEHUB-CLIENT.ps1`, qui conservait systématiquement ce dossier. |

Code : `src/GameSaveHub.Client.Setup/Program.cs` (aiguillage des modes), `Installer.cs`, `Updater.cs`, `Uninstaller.cs`, `ScheduledTaskManager.cs`.

## Les deux nouveaux endpoints serveur

- `GET /api/v1/client/latest` — **non authentifié** (un poste tout juste installé n'a pas encore d'identité enrôlée). Renvoie `version`, `sha256` du paquet, `signature` ECDSA du manifeste et `downloadUrl`.
- `GET /api/v1/client/packages/{version}` — **non authentifié**, sert le paquet `.zip` immuable via `ClientReleaseObjectStore`, un magasin sœur d'`ImmutableArtifactStore` — même racine `data/objects/` et même principe d'adressage par contenu, mais sans la validation d'enveloppe `.gshsave` puisqu'un paquet client n'est pas une sauvegarde de jeu (sous-arborescence dédiée `data/objects/client-releases/`).
- `POST /api/v1/device/revoke-self` — **authentifié** (même signature d'identité machine CNG que les autres requêtes). `204` en cas de révocation réussie ; `409 device_has_active_session` si le device détient une session serveur active, en protection côté serveur en complément de la vérification locale de `maintenance-status`.

## Les deux nouvelles commandes admin

- `client-release sign` — tourne **localement sur le poste de Steven**, avec un fichier de clé privée. Ne touche jamais le NAS ni la base de données.
- `client-release publish` — tourne **sur le NAS**, vérifie la signature du manifeste contre `GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM`, stocke l'objet et insère la ligne en base. Publier une version reste un acte administratif explicite, jamais automatique.

## Contraintes à respecter absolument

- **La clé privée de signature n'est jamais commitée ni déployée sur le NAS.** Elle reste uniquement sur le poste de Steven. Seule la clé publique (constante compilée dans `GameSaveHub-Setup.exe`, et variable d'environnement `GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM` côté service `admin`) circule.
- **La bascule ne se fait que par renommage de dossier**, jamais par écriture fichier par fichier dans l'installation active : `Client` → `Client.old` puis `Client.new` → `Client`, chaque renommage étant atomique côté NTFS. Rien n'est modifié tant que téléchargement, vérification de hash et vérification de signature n'ont pas tous réussi en préparation (`update-staging`).
- **`maintenance-status` (lecture seule, exposé par tube nommé depuis le Lot 2) gate à la fois `--auto-update` et `--uninstall`.** Aucun des deux modes ne procède si `SafeToUpdate=false` — jeu ouvert, session active, transition en cours ou checkpoint non durable font systématiquement reporter l'opération plutôt que de risquer une écriture WGS interrompue.
- **La clé publique embarquée aujourd'hui est une clé de TEST**, celle des fixtures de la Tâche 1 (`src/GameSaveHub.Client.Setup/ClientReleasePublicKey.cs`). Elle doit être remplacée par la vraie clé publique de production avant toute publication réelle — c'est le premier item bloquant de [`LOT3-VALIDATION-CHECKLIST.md`](LOT3-VALIDATION-CHECKLIST.md).

## Hors périmètre (rappel de la conception)

Pas de certificat Authenticode commercial, pas de rollback automatique multi-versions, pas de déclenchement de mise à jour poussé depuis le NAS, pas de révocation forcée à distance par l'admin (le déclenchement reste toujours côté poste concerné), pas de distribution au-delà du groupe de joueurs déjà enrôlés. Détails complets : §9 de la spécification.

## Où en est l'automatisé

`dotnet build GameSaveHub.slnx` et `dotnet test GameSaveHub.slnx` sont verts au moment où cette page est écrite (voir la sortie de vérification finale de la Tâche 13 dans le plan). Rien de ce qui précède n'a encore été exécuté sur un vrai PC Windows, une vraie clé de production ou un vrai environnement NAS — voir [`LOT3-VALIDATION-CHECKLIST.md`](LOT3-VALIDATION-CHECKLIST.md).
