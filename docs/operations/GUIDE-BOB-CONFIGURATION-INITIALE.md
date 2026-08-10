# Guide pour Bob — configuration initiale de GameSave Hub

Ce document a deux parties : ce que **Steven** doit préparer et envoyer, puis un guide à transmettre **tel quel à Bob** (à partir de la section « Bonjour Bob »).

## À l'intention de Steven — ce qu'il faut envoyer

1. **Le paquet installeur**, exactement celui déjà utilisé sur votre propre PC (même écriture activée, puisque Bob doit pouvoir héberger à son tour) :
   - Fichier : `artifacts\GameSaveHub-Client-Lot2-0.4.0-PILOTE-win-x64.zip`
   - Empreinte à communiquer avec le fichier (paquet reconstruit après les deux correctifs trouvés pendant votre propre pilote — c'est celui-ci, et aucun autre, qui a été réellement validé) :
     ```
     sha256:b992caa5e1135106089dacad55e1e5eb36d89fe94687757d60aef46879874572
     ```
     **Recalculez-la vous-même avant l'envoi** avec `Get-FileHash -Algorithm SHA256` pour être certain qu'elle correspond au fichier que vous envoyez réellement — pas seulement à ce qui est écrit ici, qui peut devenir obsolète si le paquet est reconstruit entre-temps.
2. **Un code d'invitation**, à générer **juste avant l'envoi** (il expire — ne le générez pas la veille) :
   ```sh
   docker compose --env-file deploy/.env -f deploy/compose.yml --profile tools run --rm admin enrollment create 120
   ```
   `120` = 2 heures de validité. Le code ne s'affiche qu'une seule fois : copiez-le immédiatement. Transmettez-le à Bob par un canal séparé du fichier ZIP (message différent), par simple prudence.
3. **Ce document** (la section « Bonjour Bob » ci-dessous), pour qu'il ait toutes les étapes sans avoir à vous les demander une par une.
4. Rien d'autre. Bob n'a besoin d'aucune sauvegarde de référence : c'est le serveur qui lui enverra automatiquement la dernière version du monde partagé pendant sa configuration initiale.

Avant d'envoyer quoi que ce soit, vérifiez qu'aucune session n'est active sur le monde et que le serveur répond (`STATUS-GAMESAVEHUB-CLIENT.ps1` ou `home-context`).

---

## Bonjour Bob 👋

Voici comment installer GameSave Hub sur ton PC. Prends ton temps, chaque étape est expliquée. Si quelque chose ne correspond pas à ce qui est décrit ici, **arrête-toi et préviens Steven** avant de continuer — rien n'est grave, mais il vaut mieux vérifier.

### Avant de commencer

- The Planet Crafter doit déjà être installé sur ton PC via le Xbox Game Pass.
- Sois connecté sur **ton propre compte Windows** (celui avec lequel tu joues normalement), pas un autre.
- Ferme complètement The Planet Crafter s'il est ouvert.
- Tu vas avoir besoin de :
  - le fichier `GameSaveHub-Client-Lot2-0.4.0-PILOTE-win-x64.zip` envoyé par Steven ;
  - le code d'invitation qu'il t'a envoyé séparément (une suite de lettres et de chiffres) ;
  - ton pseudo exact dans la sauvegarde : **`BoB XiMe`** (avec cette casse précise — majuscules/minuscules comprises).

### Étape 1 — Vérifier le fichier reçu

Avant d'installer quoi que ce soit, vérifie que le fichier n'a pas été altéré pendant l'envoi. Ouvre PowerShell (pas besoin d'administrateur pour ça) dans le dossier où tu as téléchargé le fichier, et tape :

```powershell
Get-FileHash -Algorithm SHA256 .\GameSaveHub-Client-Lot2-0.4.0-PILOTE-win-x64.zip
```

Compare la longue suite de lettres/chiffres affichée avec celle que Steven t'a donnée. **Si elles ne correspondent pas exactement, n'installe rien** et préviens-le.

### Étape 2 — Extraire le fichier

Clic droit sur le ZIP → « Extraire tout... ». Tu obtiens un dossier `GameSaveHub-Client-Lot2-0.4.0-PILOTE`.

### Étape 3 — Installer

1. Ouvre le dossier extrait.
2. Fais un clic droit sur **`INSTALLER-GAMESAVEHUB-PILOTE.cmd`** → **« Exécuter en tant qu'administrateur »**.
3. Windows va te demander une confirmation (fenêtre bleue/grise « Voulez-vous autoriser cette application... ») — accepte.
4. Une fenêtre noire s'ouvre, affiche quelques lignes, puis se termine sur **« INSTALLATION RÉUSSIE »**. Appuie sur une touche pour fermer.
5. L'application GameSave Hub s'ouvre automatiquement.

Si une fenêtre affiche une erreur au lieu de « INSTALLATION RÉUSSIE », **fais une capture d'écran et envoie-la à Steven** sans réessayer plusieurs fois.

### Étape 4 — S'enregistrer (une seule fois)

La première fenêtre de l'application te demande trois choses :

| Champ | Ce qu'il faut mettre |
|---|---|
| Nom de ce PC | Ce que tu veux, ça n'a pas d'importance (déjà pré-rempli) |
| Ton pseudo Planet Crafter | Exactement `BoB XiMe` |
| Code d'invitation | Celui que Steven t'a envoyé |

Clique sur **Continuer**.

### Étape 5 — Configuration unique de ton PC (deux lancements du jeu)

C'est la seule fois où cette étape sera nécessaire sur ton PC.

1. L'accueil affiche **« Configurons ce PC »**. Clique sur **Configurer ce PC**.
2. L'écran passe à **« Configuration unique — étape 1 sur 2 »**. Un nom est affiché dans une case, avec un bouton **« Copier le nom »** — clique dessus (le nom est copié dans le presse-papiers, un texte « Copié » apparaît brièvement).
3. Clique sur **Lancer The Planet Crafter**.
4. Dans le jeu, crée un **nouveau monde**. Au moment de lui donner un nom, colle celui que tu viens de copier (`Ctrl+V`) — il doit être **exactement identique**, ne le modifie pas.
5. Entre une fois dans ce monde (juste pour qu'il soit vraiment créé), puis **sauvegarde et ferme complètement** The Planet Crafter (pas juste réduire la fenêtre — quitte le jeu entièrement).
6. Reviens sur GameSave Hub : l'écran affiche **« Installation du monde partagé… »** puis, après quelques secondes, **« Configuration unique — étape 2 sur 2 »**.
7. Clique de nouveau sur **Lancer The Planet Crafter**. Cette fois, le monde qui s'ouvre contient déjà la dernière sauvegarde commune — c'est normal, c'est fait pour ça.

À partir de maintenant, ton PC est configuré : plus jamais besoin de créer un monde ni de recopier un nom.

### Étape 6 — Utilisation au quotidien

- **Pour prendre la main** (récupérer la dernière version et jouer) : ouvre GameSave Hub, clique sur **Prendre la main**, attends que ça affiche **« Tout est prêt pour jouer »**, puis **Lancer The Planet Crafter**. Joue, sauvegarde, ferme complètement le jeu quand tu as fini — GameSave Hub publie automatiquement ta progression, pas besoin de faire quoi que ce soit de plus.
- **Pour rejoindre quelqu'un d'autre qui héberge** (Steven, par exemple) : GameSave Hub te l'indique directement à l'accueil et te propose de lancer le jeu ; rejoins-le ensuite depuis le menu multijoueur du jeu, comme d'habitude.

### En cas de doute

Rien de ce qui précède ne modifie ta sauvegarde tant que tu n'as pas cliqué sur un bouton. Si un écran ne correspond pas à ce guide, ou si un message d'erreur s'affiche, **arrête-toi, fais une capture d'écran, et préviens Steven** avant de recommencer.
