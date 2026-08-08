# Journal de déploiement — Phase 3 / API 0.3.0

Relevé factuel de ce qui a été exécuté sur le NAS `TNASSPA` (`192.168.1.73`), avec les identifiants réellement produits. Ce document est la référence pour les étapes suivantes et pour un éventuel retour arrière.

## 8 août 2026 — Paliers A, B et C

### Palier A — image API 0.3.0

Le chemin « Build image » de Portainer a été abandonné au profit de GitHub Actions → GHCR (voir [le diagnostic](../etat-des-lieux-2026-08-08/08-DIAGNOSTIC-NAS.md)).

```text
image     ghcr.io/stevenpwlk/gamesavehub-api:0.3.0
digest    sha256:243198903567bc4b6b4d0fe6f05416ac95fc86af9c672bc58548646e01cd725d
taille    130 MB
build     .github/workflows/api-image.yml, 96/96 tests
```

Le digest a été vérifié trois fois : à la publication par la CI, par interrogation anonyme du registre avant le pull, et par `RepoDigests` sur le NAS après le pull.

**Sauvegarde préalable** — API arrêtée pendant la copie, ce qui a permis à SQLite de fusionner le WAL et de produire un fichier unique et cohérent :

```text
/Volume2/gamesavehub/backups/pre-api-0.3.0-20260808/gamesavehub.db   225 280 octets
```

**Bascule** — remplacement de la seule ligne `image:` dans l'éditeur de stack Portainer. Résultat : `healthy`, 0 redémarrage, `FeatureGates__AllowHostTransfer=false`, aucune migration SQLite en attente.

**Rollback disponible** : `gamesavehub-api:0.2.0` est conservée localement sur le NAS. La Phase 3 n'ajoutant aucune migration, le retour arrière est direct.

### Palier B — validation en lecture seule

`tools/TEST-NAS-PHASE3-READONLY.ps1`, exécuté avant et après le palier C :

| Contrôle | Résultat |
|---|---|
| DNS `saves.stevenpwlk.fr` | `90.45.82.75` |
| TCP 18443 | OK |
| `/healthz` | 200 `Healthy` |
| `/api/v1/worlds` sans jeton | **401** |
| `/api/v1/worlds/{id}/preview` sans jeton | **401** |

Les deux `401` confirment que les routes 0.3.0 sont en service : en 0.2.0 elles renvoyaient `404`.

### Palier C — monde pilote `Shlags1`

La base ne contenait **aucun** monde avant cette opération (`world list` vide, code de sortie 0).

```text
WORLD_ID     9b3f5b3f-02f7-4401-a980-d2513bda677d    Shlags1
VERSION_ID   32a23472-6ef2-41e9-8c29-10e7e2046255
SHA-256      30af9efca4bed6b7042c7dae4f83fedaa8fc9311c22153735d3a00fc96d76495
taille       31 668 octets
créée le     2026-08-08T16:02:25Z
```

L'artefact `SHLAGS1-CANONICAL-ROUNDTRIP-20260807.gshsave` a été déposé dans `/Volume2/gamesavehub/imports/` et son empreinte vérifiée **avant** l'import (identique à la copie versionnée dans `docs/evidence/`).

Objet publié dans le stockage immuable, adressé par hash :

```text
/Volume2/gamesavehub/data/objects/30/af/30af9efc….gshsave
```

Son empreinte relue sur disque après publication est conforme. Le répertoire `pending/` est vide — aucune publication interrompue.

`scp` ne fonctionne pas vers ce NAS (`Connection closed`, pas de sous-système SFTP). Le transfert s'est fait par `ssh 'cat > fichier'`, à retenir pour les prochains dépôts.

## Joueurs de l'artefact courant

| Pseudo | ID | Hôte | Inventaire | Équipement |
|---|---|---|---|---|
| `Stevenpwlk` | 0 | oui | 3 | 4 |
| `Maxdrake59` | 4 | non | 7 | 8 |
| `BoB XiMe` | 7 | non | 5 | 6 |

Un PC ne pourra prendre la main que si son pseudo configuré correspond exactement à l'un de ces trois, une seule fois.

## Verrous — état au terme du palier C

| Verrou | Emplacement | Valeur |
|---|---|---|
| `FeatureGates__AllowHostTransfer` | NAS, vérifié dans l'environnement du conteneur | `false` |
| `ClientService:EnableWgsTransfer` | PC | `false` |

Aucune écriture WGS n'est possible. Le palier D s'exécute intégralement dans cet état.

### Palier D — client réel sur PC-STEVEN ✅

Validé le 8 août. Deux défauts bloquants ont dû être corrigés avant d'y arriver, tous deux invisibles hors exécution réelle — voir [la revue critique](../etat-des-lieux-2026-08-08/07-REVUE-CRITIQUE.md).

| Contrôle | Résultat |
|---|---|
| Service `GameSaveHubClient` | `Running`, démarrage automatique |
| SID joueur résolu | `S-1-5-21-…-1001`, `AppData` correct |
| Service local / Serveur NAS | `Connecté` / `Healthy` |
| Catalogue | `Shlags1 — Available` |
| Joueurs affichés | `Stevenpwlk` ID 0 hôte, `Maxdrake59` ID 4, `BoB XiMe` ID 7 |
| Préflight `Stevenpwlk` | ✅ compatible |
| Préflight pseudo inexistant | ✅ `player_not_found`, transfert bloqué |
| Bouton de transfert | absent — verrou local fermé |
| Écriture WGS | aucune |

L'installateur en un clic `INSTALLER-GAMESAVEHUB.cmd` a été validé de bout en bout, élévation UAC comprise. C'est celui que recevra le second PC.

### Sauvegardes

```text
/Volume2/gamesavehub/backups/
  pre-api-0.3.0-20260808/            point de retour de la bascule d'image
  2026-08-08-avant-phase4/           complet, relu et vérifié exploitable
  2026-08-09-avant-ouverture-verrous/ complet
```

Les deux dernières contiennent `gamesavehub.db` **et** `objects/`. Leur base a été relue avec l'outil d'administration sur copie temporaire : `Shlags1` et sa version y figurent bien. `pre-api-0.2.0-20260807` a été supprimée, superflue.

**L'arrêt de l'API pendant la copie n'est pas optionnel** : le 8 août, `gamesavehub.db` datait d'avant la création de `Shlags1`, tout le travail résidant dans le `-wal`. Une copie à chaud aurait produit une sauvegarde silencieusement amputée.

### Filet de sécurité côté NAS

La version initiale est sanctuarisée contre la rétention :

```text
version protect 32a23472-6ef2-41e9-8c29-10e7e2046255
```

En cas de publication ratée, le retour se fait par `world restore <worldId> <versionId> <justification>`, qui revérifie l'empreinte de l'objet immuable et refuse si le monde est verrouillé. Les versions étant immuables et additives, une mauvaise capture n'écrase jamais la précédente.

## Reste à faire

**Phase 4** — ouverture des deux verrous pour le pilote `Steven ↔ Bob`, puis les preuves manquantes du [GO-NOGO](../operations/GO-NOGO.md) : deux cycles `A → B → A` reproductibles, un cycle avec redémarrage Windows, une interruption volontaire pendant l'envoi, et la conduite à tenir face à un dialogue Xbox Local/Cloud.

## Hygiène en attente

- sauvegarde automatique de `data/` avec checkpoint WAL (aujourd'hui manuelle) ;
- limites `mem_limit`/`cpus` sur les quatre services — le NAS héberge aussi la stack *arr, Jellyfin et un client torrent, pour 7,5 Gio de RAM au total ;
- `/Volume2/gamesavehub/data` est en `777`, `750` suffirait en conservant l'accès à l'uid `100` ;
- révocation du device de test `bf7e13ed-0ad1-4aca-8ee2-8cd4d3826991` et suppression des stacks temporaires ;
- suppression des 6 mondes de test sur `PC-STEVEN`, **exclusivement depuis l'interface du jeu, en ligne**.
