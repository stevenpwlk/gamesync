# Déploiement NAS TerraMaster / Portainer

## Ce qui est déployé

La stack expose uniquement Traefik. L'API et la CLI d'administration restent sur des réseaux Docker privés. Aucun socket Docker n'est monté dans Traefik.

- Internet : `saves.stevenpwlk.fr:18443` → Livebox NAT TCP → `NAS:8443` → Traefik.
- LAN : `saves.stevenpwlk.fr:18443` → DNS local → `NAS:18443` → Traefik.
- Les autres services du NAS (stack *arr, Jellyfin, client torrent) et le port `443` restent inchangés.
- Le transfert d'hôte est désactivé par défaut avec `GSH_ALLOW_HOST_TRANSFER=false`.

## Prérequis à relever

Avant le déploiement, noter :

1. l'IPv4 LAN réservée du NAS ;
2. le volume TOS confirmé (`/Volume2/gamesavehub`) ;
3. une adresse e-mail pour Let's Encrypt ;
4. un accès Portainer permettant de construire une stack depuis ce dépôt ;
5. les deux jeux d'identifiants OVH décrits ci-dessous.

## Identifiants OVH strictement séparés

### DynHost

Dans OVHcloud, créer un enregistrement DynHost A pour `saves.stevenpwlk.fr`, TTL 60, puis un utilisateur DynHost limité à cette seule sous-zone. Placer son identifiant et son mot de passe dans `dynhost_username` et `dynhost_password`.

L'updater utilise l'URL officielle actuelle `https://dns.eu.ovhapis.com/nic/update` sans paramètre `myip`; OVH déduit donc l'IPv4 source. Voir la [documentation DynHost OVHcloud](https://docs.ovhcloud.com/fr/guides/web-cloud/domains/dns-dynhost).

### DNS-01 Traefik

Créer un second jeu d'identifiants, exclusivement pour les challenges ACME. Ne jamais réutiliser le compte DynHost ni les identifiants du compte OVH principal.

Les variables attendues sont `OVH_APPLICATION_KEY`, `OVH_APPLICATION_SECRET` et `OVH_CONSUMER_KEY`, chacune injectée par fichier. Les droits minimaux documentés par lego sont POST et DELETE sur `/domain/zone/*`; limiter en plus la ressource à la zone `stevenpwlk.fr` lorsque la politique OVH le permet. Voir le [provider OVH de lego](https://go-acme.github.io/lego/dns/ovh/) et la [configuration DNS-01 Traefik](https://doc.traefik.io/traefik/user-guides/docker-compose/acme-dns/).

## Préparer les répertoires

Sur le NAS, créer :

```text
/Volume2/gamesavehub/
  data/
  letsencrypt-staging/
  letsencrypt-production/
  secrets/
```

Créer dans `secrets/` les six fichiers décrits dans `deploy/secrets/README.md`. Pour `gsh_signing_key`, utiliser au minimum 32 octets aléatoires encodés en Base64, par exemple depuis un PowerShell local :

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

Limiter les droits des dossiers à l'administrateur Docker/TOS et au moteur Docker. Les fichiers ACME doivent rester privés; Traefik les crée dans les deux dossiers `letsencrypt-*`.

Les images API et DynHost utilisent l'utilisateur non-root `gamesavehub` (`uid=100`, `gid=100`). Avec les secrets Compose basés sur des fichiers hôte, conserver le mode `600` et attribuer les six fichiers à `100:100` avant le démarrage :

```sh
chown 100:100 /Volume2/gamesavehub/secrets/*
chmod 600 /Volume2/gamesavehub/secrets/*
```

Cette attribution n'expose pas les secrets aux autres utilisateurs du NAS : seuls `root` et l'identité numérique montée dans les conteneurs peuvent les lire.

## Premier déploiement

1. Copier `deploy/.env.example` vers `deploy/.env` et remplacer l'e-mail, les chemins TOS et les valeurs nécessaires.
2. Conserver le serveur ACME de staging, utiliser `letsencrypt-staging/` et garder `GSH_ALLOW_HOST_TRANSFER=false`.
3. Construire les images et créer la stack depuis `deploy/compose.yml`.
4. Appliquer explicitement la migration avant de promouvoir l'API :

   ```sh
   docker compose --env-file deploy/.env -f deploy/compose.yml --profile tools run --rm admin database migrate
   ```

5. Démarrer ou redémarrer la stack, puis vérifier `api`, `traefik` et `dynhost`.
6. Créer le monde logique :

   ```sh
   docker compose --env-file deploy/.env -f deploy/compose.yml --profile tools run --rm admin world create Shlags1
   ```

Avec Portainer uniquement, lancer l'image `gamesavehub-admin` comme conteneur ponctuel, monter exactement le même volume vers `/data`, définir `GSH_CONNECTION_STRING`, puis remplacer la commande par `database migrate`. Le conteneur doit sortir avec le code 0.

## Livebox et résolution LAN

Créer une seule règle NAT :

```text
TCP 18443 externe → IPv4 réservée du NAS : TCP 8443
```

Ne créer aucune règle sur `443`. Pour le LAN, créer un override DNS `saves.stevenpwlk.fr` vers l'IPv4 du NAS dans le résolveur local (AdGuard Home, Pi-hole, DNS du routeur s'il le permet). Le NAS expose également `18443`, donc l'URL est identique sur tous les PC.

Si aucun DNS local n'est disponible, tester d'abord le hairpin NAT de la Livebox. Un fichier `hosts` par PC est possible pour le pilote, mais pas recommandé comme solution durable.

## Validation TLS et accès distant

1. En staging, vérifier dans les logs Traefik que le challenge DNS-01 aboutit. Le certificat de staging n'est volontairement pas reconnu par Windows.
2. Depuis le LAN, vérifier la résolution et `https://saves.stevenpwlk.fr:18443/healthz`.
3. Depuis un téléphone en 4G/5G avec Wi-Fi coupé, répéter le test.
4. Retirer `ACME_CA_SERVER`, basculer `GSH_ACME_PATH` vers `letsencrypt-production/`, puis redéployer. Ne jamais réutiliser l'état ACME de staging pour la production.
5. Vérifier le certificat, le renouvellement et l'absence d'écoute GameSave Hub sur `443`.

## Sauvegarde NAS

Sauvegarder ensemble, avec la stack arrêtée ou après checkpoint WAL :

- `data/gamesavehub.db`, `gamesavehub.db-wal` et `gamesavehub.db-shm` ;
- `data/objects/` et `data/pending/` ;
- `letsencrypt-production/acme.json` ;
- la configuration de stack, sans inclure les secrets dans une archive non chiffrée.

Une restauration n'est validée qu'après démarrage sur un dossier isolé, vérification des hashes et lecture d'une version via l'API.
