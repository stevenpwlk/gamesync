# 08 — Diagnostic du NAS et de l'échec de build Portainer

> ⚠️ **Archive figée au 8-9 août 2026, non mise à jour.** Pour l'état réel actuel du projet, voir [`docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`](../operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md).

Relevé effectué le 8 août 2026 par SSH, **en lecture seule**, sur `TNASSPA` (`192.168.1.73`, Linux 6.1.120+).

## Ce que valait l'hypothèse de départ

L'échec `Failure / Unable to build image` sans aucun log était attribué, au choix, à un problème de contexte, de réseau vers MCR, d'espace disque ou de Portainer lui-même. Le relevé tranche trois de ces quatre pistes.

| Hypothèse | Verdict | Preuve |
|---|---|---|
| Espace disque insuffisant pour Docker | ❌ écartée | `data-root` sur `/Volume2` : **1,3 To libres** |
| Le NAS ne peut pas tirer les images .NET | ❌ écartée | `mcr.microsoft.com/dotnet/sdk:10.0-alpine` (743 Mo) et `aspnet:10.0-alpine` (121 Mo) sont **déjà en cache local** depuis 3 semaines — le build n'avait rien à télécharger |
| Moteur Docker en mauvais état | ❌ écartée | Docker 27.2.1, API 1.47, 55 images, 12 conteneurs actifs, stack `gamesavehub` intégralement `healthy` |
| **Contexte trop volumineux pour `/tmp`** | ✅ **confirmée pour le premier échec** | voir ci-dessous |

## Cause confirmée du premier échec (contexte de 501 Mo)

Portainer **2.24.0** tourne en natif sur le NAS (application TOS), pas dans un conteneur :

```text
/Volume1/@apps/Portainer/portainer --bind=:19000 --tunnel-port=18000 -d /Volume1/@apps/Portainer
```

Son processus n'a **aucune variable `TMPDIR`, `TMP` ni `TEMP`** définie. Un programme Go retombe alors sur `/tmp` par défaut. Or :

```text
tmpfs  512M  44M utilisés  469M disponibles  /tmp
```

`/tmp` est un **tmpfs de 512 Mo en mémoire vive**, avec 469 Mo libres.

Quand on téléverse un contexte de build, Portainer l'écrit d'abord dans un fichier temporaire, puis le décompresse pour y localiser le Dockerfile. Le contexte de **501 Mo** — celui que produisait `build-integrated-phase3.ps1` avec les `bin/` et `obj/` — ne pouvait donc pas tenir, encore moins une fois décompressé. L'échec survenait **avant** que Docker ne soit sollicité, ce qui explique exactement l'absence totale de sortie dans l'onglet *Output*.

## Ce qui reste inexpliqué

Le contexte allégé de **570 Ko** tient très largement dans `/tmp` et a pourtant échoué de façon identique. Cette seconde défaillance n'a **pas** d'explication confirmée :

- aucune trace dans `journalctl -u docker` ni dans `/var/log/messages` sur la plage horaire concernée ;
- aucun événement OOM dans `dmesg` ;
- le contexte lui-même est sain (SHA-256 conforme, `src/GameSaveHub.Server.Api/Dockerfile` présent, aucun `bin/` ni `obj/`).

Les images de base étant en cache et le disque abondant, les causes plausibles restantes tiennent au chemin de code « Build image » de Portainer 2.24.0 lui-même. **La question n'a pas été tranchée** — et elle a cessé d'être bloquante, la chaîne GitHub Actions → GHCR remplaçant complètement ce chemin.

Trancher définitivement demanderait un `docker build` lancé directement sur le NAS avec le contexte allégé, ce qui sort du périmètre de lecture seule.

## Configuration relevée

```text
Docker            27.2.1 (API 1.47), storage driver btrfs
data-root         /Volume2/@apps/DockerEngine/DockerData
daemon.json       /Volume2/@apps/DockerEngine/conf/daemon.json
                  live-restore: true, bip 172.17.0.1/16, aucun registry-mirror
binaire           /Volume2/@apps/DockerEngine/dockerd/bin/docker
Portainer         2.24.0, natif, données sur /Volume1
```

Systèmes de fichiers :

| Point de montage | Taille | Libre |
|---|---|---|
| `/` | 7,5 Go | 5,1 Go |
| `/Volume1` | 229 Go | 205 Go |
| `/Volume2` | 3,7 To | 1,3 To |
| `/tmp` (tmpfs) | **512 Mo** | 469 Mo |

État de la stack au moment du relevé :

```text
gamesavehub-api-1       gamesavehub-api:0.2.0        Up 25 hours (healthy)
gamesavehub-traefik-1   gamesavehub-traefik:0.1.0    Up 25 hours (healthy)
gamesavehub-dynhost-1   gamesavehub-dynhost:0.1.0    Up 25 hours
```

`/Volume2/gamesavehub/data` : 320 Ko (`gamesavehub.db` + WAL + SHM), propriétaire `100:100` conformément à l'utilisateur non-root des conteneurs. Aucun répertoire `objects/` ni `pending/` : aucun artefact n'a encore été publié.

## Points d'hygiène relevés au passage

Aucun n'est bloquant, tous méritent une décision.

**Le compte SSH est `uid=0`.** `stevenpwlk` est root sur le NAS malgré son nom (`uid=0(stevenpwlk) gid=0`, groupes `allusers` et `admin`). Toute clé déposée dans son `authorized_keys` donne un accès root complet. À garder en tête avant d'en ajouter une autre.

**`/tmp` limité à 512 Mo en RAM.** Cette contrainte ne concerne pas que Portainer : toute opération TOS passant par un fichier temporaire volumineux échouera de la même manière, probablement avec des messages tout aussi peu explicites.

**`/Volume2/gamesavehub/data` est en `drwxrwxrwx`.** Le répertoire contenant la base SQLite et, à terme, les artefacts de sauvegarde est accessible en écriture à tous les comptes du NAS. `750` suffirait, à condition de conserver l'accès à l'uid `100` utilisé par les conteneurs.

**Effet de bord du correctif SSH.** Le répertoire personnel était en `777`, ce qui faisait rejeter l'authentification par clé par `StrictModes`. Le `chmod g-w,o-w` a résolu le problème mais a mécaniquement abaissé le masque ACL de `rwx` à `r-x` :

```text
avant :  group:admin:rwx    mask::rwx
après :  group:admin:rwx    #effective:r-x    mask::r-x
```

Le propriétaire conserve l'écriture (l'entrée `user::rwx` n'est pas soumise au masque), mais **le groupe `admin` a perdu le droit d'écriture** sur `/home/stevenpwlk`. Sans conséquence connue à ce jour ; à rétablir par `setfacl -m mask::rwx /home/stevenpwlk` si un usage s'en trouve gêné — au prix du retour du refus d'authentification par clé.

## Conséquence pratique

Le correctif apporté à `tools/build-integrated-phase3.ps1` (exclusion de `bin/` et `obj/`, refus d'un contexte supérieur à 20 Mo) traitait déjà la cause confirmée. La chaîne GitHub Actions → GHCR supprime purement et simplement le besoin de téléverser un contexte au NAS : celui-ci ne fait plus qu'un `docker pull`.
