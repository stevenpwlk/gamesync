# Checklist de validation — Lot 3

Toutes les cases ci-dessous nécessitent une exécution réelle sur un PC Windows physique,
avec accord explicite avant toute installation, tâche planifiée ou écriture WGS réelle.

## Préalable bloquant

- [x] Steven a généré sa propre paire de clés ECDSA P-256 de production (hors dépôt,
      hors NAS, hors OneDrive) et remplacé la clé publique de test dans
      `src/GameSaveHub.Client.Setup/ClientReleasePublicKey.cs` par la vraie clé publique
      (commit `b7dfb2f`, 2026-08-11).
- [x] Décision prise sur la diffusion de la même clé publique côté NAS : puisque le
      conteneur `admin` tourne en ponctuel (`docker run` à la demande, jamais comme
      service Portainer persistant), la variable `GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM` sera
      passée directement sur la ligne de commande à chaque exécution réelle de
      `client-release publish`, plutôt que stockée en permanence dans le stack — décision
      validée avec Steven le 2026-08-11. `deploy/compose.yml` documente la variable
      requise (commit `9c4f59c`) pour quiconque déploierait `admin` en service persistant
      un jour. **Reste à vérifier à la première exécution réelle de `client-release
      publish`, plus bas dans cette checklist.**

## Installation

- [x] `GameSaveHub-Setup.exe` installe avec succès (2026-08-11, PC-STEVEN, poste pilote
      déjà en production — accord explicite obtenu, sauvegarde de `%ProgramData%\GameSaveHub`
      faite avant). L'exécutable ne s'élève pas tout seul : il faut le relancer avec
      élévation explicite (`Start-Process -Verb RunAs`), noté ici pour la prochaine fois.
      Vérifié après coup : service `GameSaveHubClient` `Running`/`Automatic`, raccourci
      Démarrer créé, tâche planifiée `GameSaveHubUpdater` présente et correctement
      configurée (`schtasks /Query /V` élevé : `\GameSaveHubUpdater`, exécute `"C:\Program
      Files\GameSaveHub\GameSaveHub-Setup.exe" --auto-update`, compte `Système`, répétition
      toutes les 6 h) — invisible depuis un `Get-ScheduledTask` non élevé à cause de son ACL
      SYSTEM, ce n'est pas un défaut.
- [x] Installé par-dessus l'installation pilote existante (Lot 2) : `client-state.json` et
      `managed-slot.json` strictement identiques avant/après (`diff` sans sortie) — deviceId
      `79fd2323-53c0-45fc-9dc0-e3e7720922d7`, pseudo `Stevenpwlk` préservés, pas de
      ré-enrôlement. Confirmé en direct via `home-context` sur le tube nommé : service sain,
      monde principal `Shlags1` correctement identifié, `managedSlotStatus` cohérent.
      `%ProgramData%\GameSaveHub\appsettings.local.json` écrit, `VERSION` = `0.5.0`.

## Mise à jour silencieuse

- [x] Version factice `0.5.1` (contenu identique à `0.5.0`, seul `VERSION` change) signée
      avec la vraie clé privée puis publiée réellement sur le NAS (2026-08-11) via
      `client-release publish`, détectée et appliquée par `GameSaveHub-Setup.exe
      --auto-update` lancé manuellement en élevé, jeu fermé, aucune session active
      (`maintenance-status` confirmé `safeToUpdate: true` juste avant). Découvertes et
      corrigées au passage : l'image `gamesavehub-api` déployée (`0.4.0`) et l'image
      `gamesavehub-admin` (`0.1.1`) étaient toutes deux en retard sur `main` — reconstruites
      en `gamesavehub-api:0.5.0` et `gamesavehub-admin:0.1.2` ; la migration EF
      `AddClientReleases` n'avait jamais été appliquée à la base réelle — appliquée
      (`database migrate`, additive uniquement) ; la limite `GSH_MAX_ARTIFACT_BYTES`
      (64 Mo par défaut) était trop petite pour un paquet Setup complet (~102 Mo, runtime
      .NET self-contained) — relevée à 200 Mo pour `api` et `admin` (commit `4941651`).
- [ ] Lancée pendant une session `InGame` : `--auto-update` ne touche à rien et se
      termine proprement (vérifier via les journaux de diagnostic). **Non testé** — nécessite
      de lancer réellement le jeu, reporté à la demande de Steven.
- [x] Après application, le service redémarre et répond au tube nommé en moins de 30 s :
      confirmé, `home-context` répond immédiatement après la bascule (2026-08-11), deviceId
      et pseudo identiques à avant (`79fd2323-53c0-45fc-9dc0-e3e7720922d7` / `Stevenpwlk`).

## Désinstallation

- [x] `--uninstall` en ligne (2026-08-11, PC-STEVEN) : révocation confirmée côté serveur —
      `device list` montre `79fd2323-53c0-45fc-9dc0-e3e7720922d7` (PC-STEVEN) passé de
      `actif` à `révoqué`. Service `GameSaveHubClient` supprimé (`Get-Service` ne le trouve
      plus), `%ProgramData%\GameSaveHub` supprimé en entier, raccourci Démarrer supprimé.
      Résidu mineur attendu : `GameSaveHub-Setup.exe` lui-même reste dans
      `%ProgramFiles%\GameSaveHub\` car verrouillé pendant qu'il s'exécute (comportement
      documenté dans le code, pas un défaut — sera écrasé à la prochaine installation).
- [ ] `--uninstall` hors ligne (Wi-Fi coupé) : suppression locale complète malgré
      l'échec de révocation, message de rappel affiché. **Nécessite un nouvel enrôlement au
      préalable** (le device précédent est révoqué) — reste à faire.

**Bug réel trouvé et corrigé pendant ce test (2026-08-11, commit `42eab12`) :** après
réinstallation, l'accueil affichait « Le slot du monde doit être vérifié » (arrêt de sûreté)
au lieu de proposer un rattachement — `ManagedSlotResolver` renvoie `UnboundCandidate` quand
le monde `GSH-MONDE-PARTAGE` existe déjà mais qu'aucun `managed-slot.json` local ne le lie
(exactement ce que produit une désinstallation complète suivie d'une réinstallation).
`HomeStatePresenter` et `ManagedSlotCoordinator.BindExistingAsync` ne géraient que
`LegacyCandidate`. Ce cas ne pouvait pas se produire avant le Lot 3 (`ProgramData` n'était
jamais entièrement supprimé). Corrigé : `UnboundCandidate` traité comme `LegacyCandidate`
(mêmes garanties de topologie déjà vérifiées par `ResolveCandidate`). Revalidé en réel sur
PC-STEVEN : `managed-slot-bind-existing` → `managed_slot_bound`, `managedSlotStatus` passé
directement à `Ready` (pas de `RenamePending`, le nom était déjà correct).

## Clôture

- [ ] `dotnet build GameSaveHub.slnx` : 0 avertissement, 0 erreur.
- [ ] `dotnet test GameSaveHub.slnx` : toutes les suites passent, total ≥ le plancher
      constaté au début de ce plan.
- [ ] Accord explicite de l'utilisateur obtenu avant toute fusion vers `main`.
