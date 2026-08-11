# Checklist de validation — Lot 3

Toutes les cases ci-dessous nécessitent une exécution réelle sur un PC Windows physique,
avec accord explicite avant toute installation, tâche planifiée ou écriture WGS réelle.

## Préalable bloquant

- [ ] Steven a généré sa propre paire de clés ECDSA P-256 de production (hors dépôt,
      hors NAS) et remplacé la clé publique de test dans
      `src/GameSaveHub.Client.Setup/ClientReleasePublicKey.cs` par la vraie clé publique.
- [ ] La même clé publique de production est configurée dans la variable d'environnement
      `GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM` du service `admin` sur le NAS
      (`deploy/compose.yml` ou l'équivalent Portainer déjà utilisé pour `gamesavehub-admin`).

## Installation

- [ ] `GameSaveHub-Setup.exe` installe avec succès sur un PC sans installation
      préexistante (service `Running`/`Automatic (Delayed Start)`, raccourci créé,
      tâche planifiée `GameSaveHubUpdater` visible dans le Planificateur de tâches).
- [ ] Installé par-dessus une installation `0.4.0-pilot` existante : identité CNG,
      pseudo enregistré et `managed-slot.json` préservés (pas de ré-enrôlement).

## Mise à jour silencieuse

- [ ] Une version factice plus récente publiée via `client-release sign` +
      `client-release publish` est détectée et appliquée par `--auto-update` lancé
      manuellement, jeu fermé et aucune session active.
- [ ] Lancée pendant une session `InGame` : `--auto-update` ne touche à rien et se
      termine proprement (vérifier via les journaux de diagnostic).
- [ ] Après application, le service redémarre et répond au tube nommé en moins de 30 s.

## Désinstallation

- [ ] `--uninstall` en ligne : révocation confirmée côté serveur (`device list` ne
      montre plus l'appareil comme actif), service/app/tâche/ProgramData supprimés.
- [ ] `--uninstall` hors ligne (Wi-Fi coupé) : suppression locale complète malgré
      l'échec de révocation, message de rappel affiché.

## Clôture

- [ ] `dotnet build GameSaveHub.slnx` : 0 avertissement, 0 erreur.
- [ ] `dotnet test GameSaveHub.slnx` : toutes les suites passent, total ≥ le plancher
      constaté au début de ce plan.
- [ ] Accord explicite de l'utilisateur obtenu avant toute fusion vers `main`.
