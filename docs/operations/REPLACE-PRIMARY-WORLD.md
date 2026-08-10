# Remplacer le monde principal avec une sauvegarde transmise par Bob

Cette procédure est administrative. Elle ne doit être exécutée qu'après validation du fichier reçu et autorisation explicite pour chaque copie ou opération sur le NAS.

**Validée réellement le 10 août 2026 :** procédure exécutée de bout en bout pour remplacer `Shlags1` par une nouvelle sauvegarde reçue de Bob (nouveau monde, `Standard-1.json` chez lui, seed différent — remplacement volontaire, pas une erreur). Sauvegarde SQLite + objets prise avant écriture, fichier copié et vérifié par hash (`sha256:291a176d...`), `world replace` exécuté avec `--source-player "BoB XiMe" --require-player "Stevenpwlk"`, nouvelle version créée et confirmée par `storage verify`/`world list`/`version list`, ancienne version conservée dans l'historique. **Point important découvert à cette occasion :** l'image admin réellement déployée sur le NAS (`gamesavehub-admin:0.1.0`) n'avait pas encore la commande `world replace` — elle a dû être reconstruite depuis les sources courantes (`docker build -f src/GameSaveHub.Server.Admin/Dockerfile`, taguée `gamesavehub-admin:0.1.1`) avant de pouvoir l'utiliser. `scp` ne fonctionne pas sur ce NAS (sous-système désactivé) ; utiliser `cat fichier | ssh ... 'cat > destination'` à la place.

## 1. Export chez Bob

Bob ferme complètement The Planet Crafter, lance `GameSaveHub.SaveExporter.exe`, choisit la sauvegarde à l'aide des joueurs et de la date, puis sélectionne un dossier de destination.

Il transmet uniquement le fichier `.gshsave` généré. Aucun dossier WGS, fichier `container.*` ou autre donnée Xbox ne doit être envoyé ou modifié manuellement.

## 2. Validation locale hors NAS

Conserver le fichier reçu dans un dossier de travail local et calculer son SHA-256 :

    Get-FileHash -Algorithm SHA256 -LiteralPath .\sauvegarde-bob.gshsave

La commande `world replace` relit ensuite l'enveloppe, le hash du payload, la topologie des joueurs et tous les pseudos imposés avant de toucher aux métadonnées SQLite.

## 3. Porte d'approbation NAS

Avant toute suite :

1. sauvegarder la base SQLite et le répertoire d'objets ;
2. vérifier qu'aucune session n'est active ;
3. relever l'ID du monde, la version courante attendue et son hash ;
4. demander l'accord explicite pour copier le seul `.gshsave` dans le dossier d'imports du NAS.

Ne pas remplacer un fichier existant et ne jamais copier directement dans WGS.

## 4. Remplacement contrôlé

Le modèle [ADMIN-REPLACE-WORLD.template.yml](../../deploy/portainer/ADMIN-REPLACE-WORLD.template.yml) monte les imports en lecture seule. Il accepte jusqu'à trois joueurs requis via `GSH_REQUIRED_PLAYER_1`, `GSH_REQUIRED_PLAYER_2` et `GSH_REQUIRED_PLAYER_3` (les deux derniers sont facultatifs). Renseigner tous ses paramètres, notamment la version courante attendue :

    world replace <world-id> <fichier.gshsave> <expected-current-version-id>
      --source-player "BoB XiMe"
      --require-player "Stevenpwlk"
      --require-player "Maxdrake59"
      --reason "Sauvegarde actuelle validée avec Bob"

Si la version courante a changé, si une session est active, si un pseudo manque ou si la topologie est incohérente, la commande refuse le basculement. L'ancienne version est protégée et le changement est audité.

## 5. Vérifications et rollback

Après le remplacement :

    storage verify
    world list
    version list <world-id>

En cas de doute, restaurer immédiatement l'ancienne version protégée :

    world restore <world-id> <ancienne-version-id> "Rollback après contrôle du remplacement"
    storage verify

La copie réelle, la migration SQLite et l'exécution Portainer sont trois actes séparés. Aucun n'est implicite dans la construction de l'application ou de l'exporteur.
