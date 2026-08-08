# Constats initiaux — PC de développement

Capture en lecture seule réalisée le 2 août 2026 pendant que le jeu était actif; elle ne constitue donc pas un snapshot cohérent.

- Package : `MijuGames.ThePlanetCrafter_1.0.34.0_x64__ta6nvwnbx9v7t`
- Famille : `MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t`
- Application déclarée : `Game`
- Exécutable de lancement déclaré : `GameLaunchHelper.exe`
- Protocoles déclarés : `ms-xbl-764362ab`, `ms-xbl-multiplayer`
- Processus observé : `Planet Crafter`
- WGS présent sous le profil local avec un répertoire utilisateur, deux sous-conteneurs, `containers.index`, des fichiers `container.*` et des blobs à nom hexadécimal.
- Inventaire observé : huit fichiers pour 113 628 octets; deux métadonnées de conteneur, un index et cinq blobs opaques.

Ces observations valident la détection initiale mais ne prouvent ni le format portable, ni la méthode d'import, ni le changement d'hôte.

## Monde jetable confirmé

- Fichier logique : `Standard-2.json`
- Nom affiché : `Shlags1`
- Seed : `569155654`
- Trois joueurs détectés avec des identifiants d'inventaire et d'équipement distincts.
- `Stevenpwlk` porte actuellement `host=true`; `Maxdrake59` et `BoB XiMe` portent `host=false`.

Le champ `host` est donc observable, mais sa modification isolée n'est pas encore autorisée : les relations avec l'identité Xbox, les inventaires et Xbox Cloud restent à démontrer.

## Expérience mono-PC n°1 — sauvegarde contrôlée

Deux snapshots cohérents et validés ont encadré une session sur `Shlags1` :

- référence : `20260802T220955Z-add8fe0bb7fb48e2a480682eb47fe4f8`;
- après session : `20260802T221405Z-6a53ad3a6cb14242884226ed84501448`;
- le déplacement de `Stevenpwlk` est retrouvé dans la section joueurs;
- son champ `host` reste `true`, et les deux autres restent `false`;
- l'inventaire `3` de `Stevenpwlk` reçoit deux objets `ice` distincts;
- les sections monde, joueurs, objets et inventaires changent; les métadonnées générales du monde restent stables.

WGS fait tourner les noms physiques lors de la sauvegarde : le blob de `Standard-2.json` change d'identifiant, `container.222` devient `container.224` et `containers.index` est mis à jour. `PlayerPrefs.json` tourne également. Par conséquent, toute synchronisation doit résoudre le nom logique via la génération courante de `container.*`; elle ne doit jamais mémoriser durablement un nom de blob hexadécimal.

## Expérience mono-PC n°2 — restauration ciblée hors ligne

- Source restaurée : snapshot de référence `20260802T220955Z-add8fe0bb7fb48e2a480682eb47fe4f8`.
- Snapshot automatique pré-restauration : `20260802T222316Z-b320d85f857941908a75aeb77b201b59`.
- Snapshot après lancement/fermeture hors ligne : `20260802T222438Z-c9a15d8379094409b3bb3a601e3c300a`.
- Le jeu démarre hors ligne et charge `Shlags1`.
- L'utilisateur confirme le retour de la position précédente et la disparition des deux objets `ice`.
- Après fermeture, `Standard-2.json` est bit pour bit identique à la source restaurée (`fe969209…`).
- Les positions, les trois indicateurs `host` et l'inventaire du joueur hôte correspondent exactement à la source.
- `Standard-1.json` et `Backup.json` restent bit pour bit inchangés.
- La simple reconnexion Internet, sans lancer Xbox ni le jeu, ne remplace pas `Standard-2.json`; seul `PlayerPrefs.json` reçoit une nouvelle génération physique avec un contenu identique.

Conclusion provisoire : le remplacement atomique du blob courant résolu par nom logique fonctionne sur le même PC hors ligne. Le comportement au prochain lancement connecté et face au cloud Xbox reste à valider.

## Expérience mono-PC n°3 — premier lancement connecté

- Après reconnexion, le lanceur Xbox affiche `Enregistrement local` avec une icône de cloud déconnecté et l'heure de la session hors ligne.
- Aucun choix entre copie locale et copie cloud n'est présenté.
- Le jeu est lancé connecté jusqu'au menu principal, sans charger de monde, puis fermé.
- Snapshot post-lancement : `20260802T223023Z-a7e4e1b86464465aa6f8391e1678b026`.
- `Standard-2.json`, `Standard-1.json`, `Backup.json` et `Achievements.sav` restent physiquement et logiquement inchangés.
- Seul `PlayerPrefs.json` reçoit une nouvelle génération; `containers.index` est actualisé.

Conclusion : le premier lancement connecté ne télécharge pas silencieusement l'ancienne version cloud du monde restauré. La prochaine étape doit charger puis fermer `Shlags1` connecté afin d'observer la publication locale vers Xbox Cloud.

## Expérience mono-PC n°4 — publication vers Xbox Cloud

- `Shlags1` est chargé connecté; la position restaurée et l'absence des deux objets `ice` sont conservées.
- Aucun dialogue de conflit local/cloud n'apparaît.
- Après fermeture, le lanceur Xbox affiche `Synchronisé — 03/08/2026 00:31`.
- Snapshot post-synchronisation : `20260802T223227Z-a4c16c71324c44da96065835b799e5a1`.
- `Standard-2.json` reste sur le contenu restauré; le cloud ne le réécrit pas.
- `Standard-1.json`, `Backup.json` et `Achievements.sav` restent inchangés.
- `PlayerPrefs.json` et `containers.index` reçoivent uniquement de nouvelles générations physiques.

Conclusion mono-PC : l'export, le remplacement atomique ciblé hors ligne, le chargement local, puis la publication de la copie locale vers Xbox Cloud fonctionnent sur le monde jetable sans affecter l'autre monde. Cela ne valide pas encore le transfert vers un autre compte ou PC, ni le changement d'hôte.
