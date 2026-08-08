# Nettoyage des éléments temporaires GameSave Hub

## 1. Mondes temporaires sur PC-STEVEN

Ne jamais supprimer un monde directement dans WGS, dans `%LOCALAPPDATA%`, avec PowerShell ou avec l'Explorateur.

La suppression doit être faite **uniquement depuis l'interface de The Planet Crafter**, avec Internet actif, pour que le jeu et Xbox Cloud gèrent ensemble les métadonnées.

### Mondes réels à conserver

- `Standard-1` — vrai monde historique de Steven.
- `Shlags1` — vrai monde de travail historique, actuellement `Standard-2.json`.

### Mondes de test désormais supprimables

D'après le dernier rapport Round-Trip validé :

| Logique | Nom affiché |
|---|---|
| `Standard-3.json` | `GSHDIAG55E319` |
| `Standard-4.json` | `GSHDIAG213E59` |
| `Standard-5.json` | `GSHXFER67CC35` (le payload peut contenir un espace final invisible) |
| `Standard-6.json` | `GSHDIAGF6710B` |
| `Standard-7.json` | `GSH-SHLAGS-RETURN` |
| `Standard-8.json` | `GSH-BOB-REAL-WORLD` |

Ces six mondes ne sont plus nécessaires : leurs rapports et artefacts utiles ont déjà été récupérés.

### Procédure

1. Fermer tous les anciens probes/outils GameSave Hub.
2. Vérifier dans l'application Xbox que The Planet Crafter ne présente pas de conflit de synchronisation.
3. Lancer The Planet Crafter **en ligne**.
4. Aller dans l'écran de sélection des sauvegardes.
5. Ne toucher ni à `Standard-1`, ni à `Shlags1`.
6. Supprimer un par un les six mondes `GSH...` ci-dessus via l'icône corbeille du jeu.
7. Revenir au menu principal puis fermer complètement le jeu.
8. Attendre que Xbox indique `Synchronisé`.
9. Relancer le jeu.
10. Vérifier qu'il ne reste que les mondes réels attendus.

En cas de doute sur un nom, **ne pas supprimer** et faire une capture d'écran.

### PC de Bob

Ne pas demander un nouveau nettoyage à Bob pour l'instant.

Son monde personnel `Standard-1` doit évidemment rester.

Les mondes temporaires créés pendant nos tests pourront être supprimés plus tard, après le premier transfert intégré `Shlags1` stocké et protégé sur le NAS. Cela évite de supprimer une copie de secours avant la fin de la transition.

---

## 2. Portainer : ce qui doit rester

La stack principale suivante doit être conservée :

`gamesavehub`

Services/images actuellement utiles :

- `gamesavehub-traefik:0.1.0`
- `gamesavehub-api:0.2.0` — API actuellement active au moment de ce document
- `gamesavehub-dynhost:0.1.0`
- `gamesavehub-admin:0.1.0`

À conserver également :

- réseau `gamesavehub_edge`
- réseau interne `gamesavehub_backend`
- `/Volume2/gamesavehub/data`
- `/Volume2/gamesavehub/secrets`
- `/Volume2/gamesavehub/letsencrypt-production`
- `/Volume2/gamesavehub/backups`

**Ne jamais utiliser `docker system prune --volumes` ou une suppression globale des volumes.**

---

## 3. Révoquer puis supprimer le probe d'authentification temporaire

DeviceId du probe réseau validé :

`bf7e13ed-0ad1-4aca-8ee2-8cd4d3826991`

Si la révocation n'a pas encore été effectuée :

1. Déployer temporairement la stack `gamesavehub-revoke-temp-device`.
2. Vérifier dans ses logs :
   `Appareil bf7e13ed-0ad1-4aca-8ee2-8cd4d3826991 révoqué.`
3. Supprimer ensuite la stack `gamesavehub-revoke-temp-device`.

La clé privée du probe était déjà éphémère, mais cette révocation nettoie correctement l'autorisation serveur.

---

## 4. Stacks temporaires supprimables

Une fois leur opération terminée, supprimer dans Portainer :

- `gamesavehub-enrollment-temp`
- `gamesavehub-revoke-temp-device`

La suppression de ces stacks doit également supprimer leurs conteneurs one-shot arrêtés.

Elles utilisent le réseau externe `gamesavehub_backend` et le bind mount de données ; **ne demander aucune suppression de volume de données**.

La stack `gamesavehub` principale ne doit pas être supprimée.

---

## 5. Conteneurs arrêtés/orphelins

Après suppression des stacks temporaires :

1. Portainer → Containers.
2. Filtrer sur `stopped` / `exited`.
3. Supprimer uniquement les conteneurs clairement associés aux stacks temporaires, par exemple :
   - `gamesavehub-enrollment-temp-*`
   - `gamesavehub-revoke-temp-device-*`
4. Ne pas supprimer :
   - `gamesavehub-api-1`
   - `gamesavehub-traefik-1`
   - `gamesavehub-dynhost-1`
   - un conteneur `admin` lancé intentionnellement pour une opération en cours.

---

## 6. Images Docker

Pour le moment, conserver :

- l'image API active `gamesavehub-api:0.2.0`;
- l'ancienne `gamesavehub-api:0.1.0` comme rollback;
- Traefik/DynHost/Admin 0.1.0.

Après validation de l'API Phase 3 (`0.3.0`) et une période de fonctionnement stable, nous pourrons supprimer progressivement les anciennes images API.

Ne pas supprimer une image simplement parce qu'elle est marquée `unused` tant qu'elle fait partie du plan de rollback.

---

## 7. Données SQLite

Les lignes historiques d'invitation, sessions et appareils révoqués peuvent rester dans SQLite. Elles sont petites et utiles pour l'audit.

Ne pas éditer `gamesavehub.db` à la main.

La rétention des versions de sauvegarde sera gérée ultérieurement avec `GameSaveHub.Server.Admin`, pas par suppression manuelle de fichiers dans `/data/objects`.
