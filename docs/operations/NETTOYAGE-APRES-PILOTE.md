# Nettoyage global après la campagne pilote

Cette liste s'exécute **une fois la campagne terminée et ses preuves consignées**, pas entre deux essais. Chaque essai laisse des traces volontaires : les supprimer trop tôt détruirait les éléments qui permettent de comprendre un échec.

Rien ici n'est urgent. Tout est petit. La raison de le faire est la lisibilité, pas la place.

## Ordre impératif

1. Les mondes de test sur les PC — **uniquement après** que le monde partagé soit publié et vérifié sur le NAS.
2. Le NAS.
3. Le poste de développement.

Ne jamais commencer par le NAS : c'est lui qui détient la copie de référence.

---

## 1. Mondes de test sur les PC

Chaque essai de transfert crée un nouveau `Standard-X` sur la machine qui reçoit. Après plusieurs essais il y en a autant.

**Ne jamais supprimer un monde depuis l'Explorateur, PowerShell ou `%LOCALAPPDATA%`.** La suppression se fait **exclusivement depuis l'écran de sélection des sauvegardes du jeu, Internet actif**, pour que le jeu et Xbox Cloud gèrent ensemble les métadonnées.

### Identifier ce qui est supprimable

```powershell
dotnet run --project src/GameSaveHub.Diagnostics -- inventory
```

Les mondes issus des essais portent un nom affiché commençant par `GSHIMPORT` — sauf s'ils ont déjà été chargés, auquel cas ils portent le nom contenu dans la sauvegarde importée (`GSH-SHLAGS-RETURN` pour la campagne d'août 2026).

### À conserver sur `PC-STEVEN`

| Monde | Raison |
|---|---|
| `Standard-1` | monde historique de Steven |
| `Shlags1` (`Standard-2.json`) | monde de travail historique |

Au 9 août, `Standard-3.json` et `Standard-4.json` portent **le même nom affiché** `GSH-SHLAGS-RETURN`. Ce n'est pas un accident : ils ont été conservés volontairement pour éprouver en conditions réelles le correctif qui désigne le monde à capturer par son nom logique et non par son nom affiché. Ils sont supprimables une fois la campagne close — mais avant de les supprimer, vérifier qu'aucune session ne les référence encore dans `%ProgramData%\GameSaveHub\transfers\*\session.json` (champ `targetLogicalName`).

### Procédure

1. Fermer toute application GameSave Hub.
2. Vérifier dans l'application Xbox qu'aucune synchronisation n'est en cours.
3. Lancer The Planet Crafter **en ligne**.
4. Supprimer un par un les mondes d'essai via l'icône corbeille.
5. Revenir au menu principal, fermer complètement le jeu.
6. Attendre que Xbox affiche `Synchronisé`.
7. Relancer le jeu et vérifier qu'il ne reste que les mondes attendus.

En cas de doute sur un nom, **ne pas supprimer** et faire une capture d'écran.

### PC du second joueur

Même procédure, mais **seulement après** confirmation que le monde partagé est publié et vérifié sur le NAS. Ses mondes d'essai sont la dernière copie de secours tant que ce n'est pas le cas.

Son monde personnel `Standard-1` reste évidemment intact.

---

## 2. NAS

### Résidus d'upload

```bash
ssh -i ~/.ssh/gamesavehub_nas -p 9222 stevenpwlk@192.168.1.73 'find /Volume2/gamesavehub/data/pending -mindepth 1 -printf "%p\n"'
```

Doit être **vide** depuis l'API 0.3.1, qui supprime les chunks après publication. Tout résidu signale une publication interrompue : l'examiner avant de l'effacer, il indique une session qui n'est pas allée au bout.

### Appareils enrôlés

```bash
ssh -i ~/.ssh/gamesavehub_nas -p 9222 stevenpwlk@192.168.1.73 '/Volume2/@apps/DockerEngine/dockerd/bin/docker run --rm -e GSH_CONNECTION_STRING="Data Source=/data/gamesavehub.db;Cache=Shared;Pooling=True" -e GSH_STORAGE_ROOT=/data -v /Volume2/gamesavehub/data:/data --network gamesavehub_backend --security-opt no-new-privileges:true gamesavehub-admin:0.1.0 device list'
```

Révoquer les appareils de test qui ne servent plus (`device revoke <id>`). Les lignes révoquées restent en base : elles sont petites et utiles à l'audit.

### Images Docker

Conserver au moins une version antérieure de l'API comme retour arrière. Ne pas supprimer une image au motif qu'elle est marquée `unused` tant qu'elle fait partie du plan de rollback.

**Ne jamais utiliser `docker system prune --volumes`.**

### Sauvegardes

Les sauvegardes de `data/` s'accumulent dans `/Volume2/gamesavehub/backups/`. Conserver au minimum :

- la dernière d'avant campagne ;
- celle précédant le dernier changement de version d'API.

Chacune pèse quelques centaines de kilo-octets. Supprimer les intermédiaires devenues sans objet.

### Verrous

**Refermer les deux verrous entre deux campagnes**, sauf décision explicite de les laisser ouverts :

| Verrou | Où | Remise à l'état sûr |
|---|---|---|
| `FeatureGates__AllowHostTransfer` | stack Portainer | `"false"` puis *Update the stack* |
| `ClientService:EnableWgsTransfer` | chaque PC | réinstaller le package **standard** |

Réinstaller le package standard est la manière la plus sûre de refermer le verrou local : elle ne laisse pas de configuration incohérente.

---

## 3. Poste de développement

### Sorties de compilation

`artifacts/` contient les packages produits et les contextes Docker. Reconstructibles par `BUILD-INTEGRATED-PHASE3.cmd`. Supprimables sans risque, à l'exception de ce qui est listé ci-dessous.

### Ce qui ne doit jamais être purgé

| Chemin | Raison |
|---|---|
| `snapshots/` | captures WGS cohérentes — seul filet en cas de perte locale |
| `docs/evidence/` | preuves expérimentales, non régénérables |
| `artifacts/transfer-*` | inventaires avant/après d'un transfert réel |
| `_archive/` | mémoire du projet hors Git |
| `%ProgramData%\GameSaveHub\transfers\` | checkpoints et journaux d'audit des sessions |

Les sessions locales terminées peuvent être supprimées **après** avoir généré et archivé le rapport de diagnostic correspondant, jamais avant.

### Journaux

`%ProgramData%\GameSaveHub\app.log` et les journaux `interruption-*.log` produits par les essais. À archiver avec les preuves de la campagne, puis supprimables.

---

## Vérification finale

```powershell
dotnet run --project src/GameSaveHub.Diagnostics -- inventory
powershell -ExecutionPolicy Bypass -File .\tools\TEST-NAS-PHASE3-READONLY.ps1
```

Attendu : seuls les mondes réels sur chaque PC, `/healthz` à 200, routes protégées à 401, `pending/` vide, et l'état des deux verrous conforme à la décision prise.
