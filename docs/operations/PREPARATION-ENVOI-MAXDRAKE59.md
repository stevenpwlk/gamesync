# Préparer l'envoi à MaxDrake59

Ce document est pour vous (Steven). Le guide destiné à MaxDrake59 est une page HTML séparée : [`GUIDE-MAXDRAKE59.html`](GUIDE-MAXDRAKE59.html) — envoyez-lui ce fichier directement (il l'ouvre en double-cliquant, aucun logiciel particulier requis).

## Ce qu'il faut envoyer

1. **Le paquet installeur**, un seul fichier ZIP contenant l'exécutable et son dossier de dépendances :
   - Fichier : `artifacts\GameSaveHub-pour-MaxDrake59.zip`
   - Empreinte à recalculer avant l'envoi avec `Get-FileHash -Algorithm SHA256` — c'est cette valeur-là qui fait foi, pas celle notée ici si le paquet a été reconstruit depuis.
   - Ce zip contient `GameSaveHub-Setup.exe` + le dossier `payload\` (Service + App) + `VERSION` — les trois doivent rester ensemble, ce n'est pas un simple exécutable autonome.
2. **`GUIDE-MAXDRAKE59.html`**, dans le même envoi ou un envoi séparé.
3. **Un code d'invitation**, généré **juste avant l'envoi** (il expire — ne le générez pas la veille), transmis par un canal séparé du fichier ZIP par simple prudence :
   ```
   ssh -i ~/.ssh/gamesavehub_nas -p 9222 stevenpwlk@192.168.1.73
   /Volume2/@apps/DockerEngine/dockerd/bin/docker run --rm \
     -e GSH_CONNECTION_STRING="Data Source=/data/gamesavehub.db;Cache=Shared;Pooling=True" \
     -e GSH_STORAGE_ROOT=/data \
     -v /Volume2/gamesavehub/data:/data \
     --network gamesavehub_backend \
     --security-opt no-new-privileges:true \
     gamesavehub-admin:0.1.2 enrollment create 120
   ```
   `120` = 2 heures de validité. Le code ne s'affiche qu'une seule fois : copiez-le immédiatement.

## Point d'attention — pseudo exact

Le pseudo de MaxDrake59 tel qu'il apparaît réellement dans le monde partagé actuel (`GSH-MONDE-PARTAGE`) est **`Maxdrake59`** (d minuscule), pas « MaxDrake59 » comme on l'écrit habituellement en conversation — vérifié directement dans l'inventaire WGS le 11 août 2026. Le rattachement du slot exige une correspondance exacte avec le nom du joueur déjà présent dans la sauvegarde.

## Avant d'envoyer

- Vérifier qu'aucune session n'est active sur le monde (`home-context` ou `STATUS-GAMESAVEHUB-CLIENT.ps1`).
- Vérifier que le serveur répond (`/healthz`).
- Recalculer vous-même le SHA-256 du fichier que vous envoyez réellement, pas seulement celui indiqué plus haut.
- Vérifier que `GET /api/v1/client/latest` pointe vers une version réelle et cohérente (pas un paquet de test) — sinon le PC de MaxDrake59 tentera une mise à jour vers ce paquet dès le premier passage de la tâche planifiée.

## Différence avec l'onboarding de Bob (Lot 2)

Contrairement à Bob, MaxDrake59 a déjà un personnage existant dans le monde partagé actuel (il a rejoint en direct une session hébergée par Bob avant cet envoi). Il n'aura donc **pas** à passer par la configuration unique en deux étapes (créer un nouveau monde nommé `GSH-MONDE-PARTAGE` dans le jeu) : l'accueil lui proposera directement **« Rattacher ce monde »**, comme cela avait été fait sur le PC de Steven lui-même lors de la première configuration du Lot 2.

## Point de vigilance — installation moins guidée qu'avant

Contrairement à l'ancien script `.cmd` (utilisé pour Bob), le nouvel exécutable `GameSaveHub-Setup.exe` :
- **ne s'élève pas automatiquement** : un double-clic simple échoue avec un message d'erreur au lieu de demander l'élévation — il faut explicitement faire un clic droit → « Exécuter en tant qu'administrateur » (indiqué dans le guide, mais plus facile à rater qu'avant) ;
- **ne rouvre pas l'application automatiquement** à la fin de l'installation — il faut la relancer depuis le menu Démarrer.

Si MaxDrake59 n'est pas très à l'aise avec Windows, envisagez de faire cette première installation avec lui en partage d'écran plutôt que de vous fier uniquement au guide écrit.

## Après l'installation de MaxDrake59

Voir [`LOT3-VALIDATION-CHECKLIST.md`](LOT3-VALIDATION-CHECKLIST.md) et [`CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`](CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md) pour le contexte de validation déjà établi. Vérifier après coup : le monde reste cohérent (mêmes mondes WGS, pas de doublon), et qu'un cycle réel de prise en main fonctionne dans les deux sens si l'occasion se présente.
