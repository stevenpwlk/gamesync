# Préparer l'envoi à MaxDrake59

Ce document est pour vous (Steven). Le guide destiné à MaxDrake59 est une page HTML séparée : [`GUIDE-MAXDRAKE59.html`](GUIDE-MAXDRAKE59.html) — envoyez-lui ce fichier directement (il l'ouvre en double-cliquant, aucun logiciel particulier requis).

## Ce qu'il faut envoyer

1. **Un seul fichier ZIP**, tout compris :
   - Fichier : `artifacts\GameSaveHub-pour-MaxDrake59.zip`
   - Empreinte à recalculer avant l'envoi avec `Get-FileHash -Algorithm SHA256` — c'est cette valeur-là qui fait foi, pas celle notée ici si le paquet a été reconstruit depuis.
   - Contenu : `GameSaveHub-Setup.exe`, le dossier `payload\` (Service + App), `VERSION`, et `LISEZ-MOI-DABORD.html` (le guide, copié depuis [`GUIDE-MAXDRAKE59.html`](GUIDE-MAXDRAKE59.html) sous ce nom explicite pour qu'il saute aux yeux une fois le zip extrait). Tout doit rester dans le même dossier après extraction — ce n'est pas un simple exécutable autonome.
2. **Un code d'invitation**, généré **juste avant l'envoi** (il expire — ne le générez pas la veille), transmis par un canal séparé du fichier ZIP par simple prudence :
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

## Quelle configuration initiale l'attend ? (corrigé le 12 août 2026)

MaxDrake59 a déjà un personnage dans le monde partagé (il a rejoint en direct une session hébergée par Bob), mais ça ne dit rien de l'état de **son propre stockage Xbox local** — rejoindre une session hébergée par quelqu'un d'autre ne crée pas forcément de sauvegarde locale persistante sur sa machine à lui. Deux cas restent donc possibles à l'installation, et impossibles à distinguer sans être devant son écran :

- **Un monde nommé `GSH-MONDE-PARTAGE` existe déjà localement chez lui, non lié** → l'accueil affichera « Un monde partagé existant a été trouvé », un seul clic sur « Rattacher ce monde ».
- **Aucun monde de ce nom n'existe encore localement chez lui** (le cas le plus probable pour un premier joueur qui n'a fait que rejoindre en invité) → l'accueil affichera « Configurons ce PC », la configuration unique en deux étapes (créer une nouvelle partie nommée exactement `GSH-MONDE-PARTAGE` dans le jeu, comme cela avait été fait pour Steven et pour Bob lors de leurs propres premières configurations).

Le guide `GUIDE-MAXDRAKE59.html` couvre les deux cas et explique lequel suivre selon ce qui s'affiche réellement à l'écran.

## Point de vigilance — installation moins guidée qu'avant

Contrairement à l'ancien script `.cmd` (utilisé pour Bob), le nouvel exécutable `GameSaveHub-Setup.exe` :
- **ne s'élève pas automatiquement** : un double-clic simple échoue avec un message d'erreur au lieu de demander l'élévation — il faut explicitement faire un clic droit → « Exécuter en tant qu'administrateur » (indiqué dans le guide, mais plus facile à rater qu'avant) ;
- **ne rouvre pas l'application automatiquement** à la fin de l'installation — il faut la relancer depuis le menu Démarrer.

Si MaxDrake59 n'est pas très à l'aise avec Windows, envisagez de faire cette première installation avec lui en partage d'écran plutôt que de vous fier uniquement au guide écrit.

## Après l'installation de MaxDrake59

Voir [`LOT3-VALIDATION-CHECKLIST.md`](LOT3-VALIDATION-CHECKLIST.md) et [`CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`](CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md) pour le contexte de validation déjà établi. Vérifier après coup : le monde reste cohérent (mêmes mondes WGS, pas de doublon), et qu'un cycle réel de prise en main fonctionne dans les deux sens si l'occasion se présente.
