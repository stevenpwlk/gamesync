# Administration locale

La CLI n'a aucune route HTTP et ne doit être exécutée que dans un conteneur ponctuel ayant accès au volume `/data`.

```text
database migrate|pending
enrollment create [durée-minutes]
device list|revoke <device-id>
world create <nom>|list
world import <world-id> <fichier.gshsave>
world replace <world-id> <fichier.gshsave> <version-courante-attendue> --source-player <pseudo> [--require-player <pseudo>] --reason <justification>
world restore <world-id> <version-id> <justification>
version list <world-id>|protect <version-id> <justification>
retention plan|purge <world-id>
session list|release <session-id> <justification>
storage verify
```

Exemple :

```sh
docker compose --env-file deploy/.env -f deploy/compose.yml --profile tools run --rm admin enrollment create 60
```

Une libération administrative exige une justification, place la session dans `Failed` et conserve l'incident. Elle ne doit être utilisée qu'après copie de sécurité et diagnostic de l'état WGS du PC concerné.

`retention plan` ne modifie rien. `retention purge` applique par défaut 20 dernières versions, 30 quotidiennes et 26 hebdomadaires. La version courante et toutes les versions protégées sont toujours conservées; un objet immuable n'est supprimé que lorsqu'aucune version ne le référence.

Le serveur ne migre jamais automatiquement SQLite au démarrage. Une image API avec migrations en attente refuse de démarrer.

Le remplacement d'un monde existant suit la procédure détaillée dans [REPLACE-PRIMARY-WORLD.md](REPLACE-PRIMARY-WORLD.md). Le modèle Portainer est fourni à titre préparatoire et ne doit pas être exécuté sans sauvegarde ni accord explicite.
