# Préparer l'envoi à Bob

Ce document est pour vous (Steven). Le guide destiné à Bob est une page HTML séparée : [`GUIDE-BOB.html`](GUIDE-BOB.html) — envoyez-lui ce fichier directement (il l'ouvre en double-cliquant, aucun logiciel particulier requis).

## Ce qu'il faut envoyer

1. **Le paquet installeur**, exactement celui déjà utilisé sur votre propre PC (même écriture activée, puisque Bob doit pouvoir héberger à son tour) :
   - Fichier : `artifacts\GameSaveHub-Client-Lot2-0.4.0-PILOTE-win-x64.zip`
   - Empreinte actuelle (recalculez-la avant l'envoi avec `Get-FileHash -Algorithm SHA256` — c'est cette valeur-là qui fait foi, pas celle écrite ici si le paquet a été reconstruit depuis) :
     ```
     sha256:b992caa5e1135106089dacad55e1e5eb36d89fe94687757d60aef46879874572
     ```
   - Ce paquet est celui reconstruit après les deux correctifs trouvés pendant votre propre pilote (`RenamePending` et la synchronisation du nom affiché). N'envoyez pas un paquet plus ancien.
2. **`GUIDE-BOB.html`**, dans le même envoi ou un envoi séparé.
3. **Un code d'invitation**, généré **juste avant l'envoi** (il expire — ne le générez pas la veille), transmis par un canal séparé du fichier ZIP par simple prudence :
   ```sh
   docker compose --env-file deploy/.env -f deploy/compose.yml --profile tools run --rm admin enrollment create 120
   ```
   `120` = 2 heures de validité. Le code ne s'affiche qu'une seule fois : copiez-le immédiatement.

Rien d'autre. Bob n'a besoin d'aucune sauvegarde de référence : c'est le serveur qui lui enverra automatiquement la dernière version du monde partagé pendant sa configuration initiale.

## Avant d'envoyer

- Vérifier qu'aucune session n'est active sur le monde (`home-context` ou `STATUS-GAMESAVEHUB-CLIENT.ps1`).
- Vérifier que le serveur répond (`healthz`).
- Recalculer vous-même le SHA-256 du fichier que vous envoyez réellement, pas seulement celui indiqué plus haut.

## Après l'installation de Bob

Voir la checklist de validation, section « Portes externes » : [`CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`](CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md). Le cycle réel `Steven → Bob → Steven` doit être exécuté et vérifié (topologie, inventaire, versions) à chaque relais avant de considérer le Lot 2 terminé.
