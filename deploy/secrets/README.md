# Secrets attendus

Créer six fichiers UTF-8 sans extension et sans ligne vide finale obligatoire :

- `gsh_signing_key` : au moins 32 octets aléatoires ;
- `ovh_application_key`, `ovh_application_secret`, `ovh_consumer_key` : compte API réservé au DNS-01 Traefik ;
- `dynhost_username`, `dynhost_password` : identifiants DynHost limités à `saves.stevenpwlk.fr`.

Ne jamais réutiliser les identifiants du compte OVH principal. Ce dossier est ignoré par Git à l'exception de ce fichier.

Sur le NAS, conserver ces fichiers en mode `600` et avec le propriétaire numérique `100:100`, utilisé par les conteneurs non-root GameSave Hub. `root` (Traefik) conserve également l'accès nécessaire.
