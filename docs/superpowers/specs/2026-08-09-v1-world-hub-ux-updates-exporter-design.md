# GameSave Hub V1 — accueil contextuel, mise à jour et exporteur

**Statut :** design validé avec l'utilisateur le 9 août 2026

**Concept visuel retenu :** proposition 2, « parcours guidé »

**Périmètre produit :** Windows 11 x64, The Planet Crafter PC Game Pass/Xbox, un joueur Xbox par PC, un seul monde partagé principal

## 1. Contexte

GameSave Hub permet à plusieurs joueurs de se transmettre l'hébergement d'un monde The Planet Crafter sans partager les métadonnées WGS propres à leurs comptes Xbox. Le pilote a déjà démontré qu'un artefact `.gshsave` peut être préparé pour un autre joueur, injecté dans un nouveau slot local, joué, puis republié sans perte observée d'inventaire, d'équipement, de position ou de progression.

L'application actuelle expose directement son fonctionnement interne : connexion au service, URL du serveur, catalogue, compatibilité, identifiants de versions, étapes de l'orchestrateur et diagnostics. Le joueur doit comprendre la mécanique avant de pouvoir jouer. La distribution du client exige aussi une intervention manuelle à chaque version.

La V1 assume explicitement un seul monde partagé principal. `Shlags1` est le monde pilote actuel, mais le hub ne doit pas être lié pour toujours à cette sauvegarde : une autre sauvegarde existante et plus récente pourra devenir la version principale par une opération administrative sûre.

## 2. Objectifs

La V1 doit :

1. répondre immédiatement à trois questions : le monde est-il disponible, qui détient la main, et que dois-je faire ;
2. réduire le parcours quotidien à une seule action dominante à la fois ;
3. adapter l'accueil au verrou distant, à l'étape locale et au processus de jeu ;
4. lancer la version Xbox/Game Pass installée de The Planet Crafter sans dépendre d'un chemin d'exécutable ;
5. permettre une installation unique et des mises à jour distantes sûres ;
6. conserver tous les invariants WGS, d'intégrité, d'exclusivité et de reprise déjà validés ;
7. fournir à Bob un mini-programme séparé, portable et strictement local pour exporter la sauvegarde de son choix ;
8. permettre à l'administrateur de promouvoir ultérieurement cet artefact comme nouvelle version principale sans perdre la version courante.

## 3. Hors périmètre

La V1 ne fournit pas :

- une bibliothèque de mondes ni un sélecteur de monde quotidien ;
- un serveur de jeu dédié ;
- l'automatisation de la connexion multijoueur dans The Planet Crafter ;
- l'interprétation automatique d'un dialogue de conflit Xbox Local/Cloud ;
- l'import ou la promotion d'une sauvegarde par un joueur ordinaire ;
- la copie manuelle de fichiers WGS ;
- la prise en charge de Steam, Windows 10, ARM64 ou des consoles Xbox.

## 4. Principes de l'expérience

### 4.1 Un écran, un monde, une décision

L'accueil ne présente que le monde principal, son état humainement compréhensible, la dernière activité utile et une action dominante. Les réglages, versions, diagnostics, hashes, seeds, identifiants WGS et détails réseau sont déplacés derrière l'engrenage.

### 4.2 Deux clics intentionnels une fois le monde préparé

Le parcours hôte sépare volontairement :

1. `Prendre la main`, qui acquiert le verrou et prépare la sauvegarde ;
2. `Lancer The Planet Crafter`, disponible seulement lorsque l'import a été validé.

Cette séparation évite qu'une consultation ou une prise de verrou accidentelle lance immédiatement le jeu. Elle ne supprime pas les étapes imposées par WGS. Tant que la réutilisation sûre d'un slot local n'est pas validée, la création d'un nouveau monde d'accueil dans le jeu reste une étape guidée lorsqu'elle est nécessaire. L'application lance alors le jeu, affiche le nom exact à utiliser, détecte sa fermeture, vérifie automatiquement le placeholder, effectue l'import, puis propose le lancement final. Aucun bouton de confirmation technique n'est demandé si l'état peut être détecté sans ambiguïté.

### 4.3 Vérité plutôt que promesse

GameSave Hub connaît l'hôte uniquement lorsqu'un joueur a acquis le monde par l'application. Si quelqu'un lance directement une ancienne copie locale, le serveur ne peut pas l'identifier comme hôte. Dans ce cas, le client détecte un jeu local actif sans session cohérente, affiche un avertissement et interdit toute écriture ou acquisition jusqu'à la fermeture du jeu.

## 5. Direction visuelle

Référence visuelle acceptée sur la machine de conception :

`C:\Users\steve\.codex\generated_images\019fe5e2-fc59-7e10-91d1-c76b22b31e50\exec-dd876d54-fba7-46c7-a72d-4a6385cc0935.png`

La direction retenue est une application Windows claire, chaude et rassurante : fond ivoire/sable, texte bleu nuit, action cobalt et succès vert mousse. Un panorama discret inspiré de The Planet Crafter donne une identité au produit sans transformer l'interface en écran de jeu.

La composition comprend :

- une barre supérieure compacte avec `GameSave Hub`, l'état général et un engrenage ;
- une progression en trois étapes : `Monde prêt`, `Prise en main`, `Jeu en cours` ;
- un centre de gravité unique avec le titre d'état, une phrase d'aide et l'action primaire ;
- une zone inférieure sobre pour l'activité récente et les joueurs ;
- aucune grille de cartes, aucune métrique décorative et aucun vocabulaire d'administration sur l'accueil.

L'interface doit rester lisible à la mise à l'échelle Windows et ne jamais dépendre d'une largeur fixe provoquant un débordement. Les textes et contrôles restent natifs WPF ; l'illustration panoramique est un actif raster séparé.

## 6. Modèle d'état contextuel

Le client combine quatre sources de vérité :

1. la santé du service local et son checkpoint persistant ;
2. le statut du monde et de la session active renvoyés par le serveur ;
3. l'identité du joueur qui détient la session ;
4. la présence locale du processus `Planet Crafter`.

Le serveur doit exposer pour la session active : l'identifiant de session, l'état, le nom du joueur, l'appareil, l'heure de création et le dernier heartbeat. Le nom du joueur est enregistré lors de l'acquisition et correspond au pseudo Planet Crafter configuré sur le client. Pour la V1 fermée à quatre joueurs de confiance, cette identité déclarative est suffisante ; l'autorisation continue de reposer sur l'identité cryptographique de l'appareil.

Le client rafraîchit l'état du monde toutes les cinq secondes. Le polling est choisi pour la V1 : il est simple, observable et suffisamment réactif pour quatre joueurs. SignalR et les notifications push sont différés.

| État observé | Message principal | Action dominante |
|---|---|---|
| Monde libre, jeu fermé, client prêt | `Le monde est prêt` | `Prendre la main` |
| Acquisition ou préparation locale | `Préparation de votre partie…` | progression, sans clic superflu |
| Placeholder requis | `Créons l'emplacement du monde` | `Lancer The Planet Crafter` puis instructions guidées |
| Import local validé | `Tout est prêt pour jouer` | `Lancer The Planet Crafter` |
| Session locale et jeu actif | `Vous hébergez la partie` | aucune action quotidienne |
| Session distante en préparation | `<Joueur> prépare le monde` | aucune acquisition possible |
| Session distante en jeu, jeu local fermé | `<Joueur> héberge actuellement` | `Lancer The Planet Crafter` |
| Session distante en jeu, jeu local actif | `The Planet Crafter est ouvert — <Joueur> héberge` | état passif |
| Capture ou publication locale | `Sécurisation de la partie…` | progression automatique |
| Publication terminée | `Le monde est de nouveau disponible` | `Prendre la main` |
| Jeu local actif sans session cohérente | `Le jeu est lancé hors de GameSave Hub` | fermer le jeu avant toute opération |
| Session récupérable interrompue | message adapté à l'étape | `Reprendre en sécurité` |
| État exigeant une expertise | `Une vérification est nécessaire` | ouvrir l'assistance, aucune écriture automatique |
| Serveur indisponible | `Le monde est momentanément inaccessible` | réessai automatique, diagnostics secondaires |

Un client qui n'est pas l'hôte peut lancer la version Xbox afin de rejoindre la partie par le parcours multijoueur habituel. Le libellé ne prétend jamais que GameSave Hub effectue la connexion au lobby.

## 7. Lancement Xbox

The Planet Crafter est enregistré sur le PC pilote avec l'AUMID :

`MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t!Game`

Le lanceur :

- découvre l'AUMID installé au lieu de figer sa valeur comme unique vérité ;
- l'active via le shell Windows afin de passer par Xbox/Gaming Services ;
- vérifie ensuite le démarrage grâce au processus déjà détecté par l'adaptateur ;
- renvoie un échec humainement compréhensible et propose d'ouvrir l'application Xbox si l'activation échoue ;
- ne lance jamais Steam.

## 8. Structure du client

Le client reste en WPF/.NET et réutilise l'orchestrateur, le service Windows et l'adaptateur existants. La refonte découpe l'actuelle fenêtre monolithique en unités indépendantes :

- `AppShell` : fenêtre, navigation minimale, état global et accessibilité ;
- `HomeViewModel` : dérivation déterministe de l'état d'accueil ;
- `OnboardingViewModel` : invitation, pseudo et préflight initial ;
- `TransferProgressViewModel` : progression guidée de la session locale ;
- `IncidentViewModel` : reprise, blocage sûr et accès aux diagnostics ;
- `SettingsViewModel` : informations techniques et maintenance ;
- `XboxGameLauncher` : découverte et activation du jeu ;
- `WorldPresencePoller` : lecture périodique, annulation et backoff ;
- `UpdateCoordinator` : politique de mise à jour et verrou de sûreté partagé.

La dérivation de l'état est une fonction testable séparée de la vue. L'IHM ne réimplémente pas les règles de transfert : elle projette l'état de l'orchestrateur et du serveur.

## 9. Installation unique et mises à jour

### 9.1 Première installation

Le joueur reçoit un seul installateur. L'élévation administrateur n'est demandée qu'à cette installation, car le produit installe un service Windows et configure ses ACL. La première ouverture demande :

1. le code d'invitation à usage unique ;
2. le pseudo Planet Crafter exact ;
3. la validation automatique du jeu Xbox, du service local et de l'accès au hub.

Une fois l'enrôlement réussi, ce parcours disparaît.

### 9.2 Architecture de mise à jour

Un composant de maintenance minimal, installé avec le client, gère les mises à jour. Les versions applicatives sont installées dans des répertoires versionnés ; le composant stable bascule le client et le service vers une version préparée plutôt que d'écraser des binaires en cours d'utilisation.

Le serveur de distribution publie un manifeste signé contenant la version, le canal, l'URL HTTPS, la taille, le SHA-256, la version minimale autorisée et la signature. Le paquet et chaque exécutable distribué sont signés. Le client vérifie la signature et le hash avant de préparer une version.

Le coordinateur refuse l'activation d'une mise à jour si :

- The Planet Crafter est actif ;
- une session locale détient le verrou du monde ;
- une importation, capture, publication ou reprise est en cours ;
- le service ne peut pas confirmer un checkpoint durable.

À un moment sûr, le composant arrête proprement l'application et le service, active la nouvelle version, démarre le service et attend son contrôle de santé. En cas d'échec, il rebascule sur la version précédente et conserve un diagnostic. Les canaux `pilot` et `stable` permettent de valider d'abord sur le PC de Steven. Une version inférieure à la version minimale autorisée peut consulter l'état mais ne peut pas acquérir le monde.

## 10. Mini-programme `GameSave Hub Export`

L'export exceptionnel d'une sauvegarde n'est pas ajouté au client principal. Un utilitaire WPF séparé et portable réutilise uniquement les contrats et l'adaptateur Planet Crafter.

Contraintes :

- exécutable autonome pour Windows 11 x64 ;
- aucune installation, aucun service, aucun droit administrateur ;
- aucun accès au hub, au NAS ou au réseau ;
- aucune écriture dans WGS ;
- aucune fonction d'import ou de restauration.

Parcours :

1. détecter l'installation Xbox et inspecter WGS ;
2. afficher les mondes avec nom, dernière modification dérivée du blob, mode et joueurs ;
3. distinguer les homonymes visuels tout en conservant le nom logique comme identifiant technique ;
4. laisser Bob sélectionner un monde ;
5. proposer `Exporter cette sauvegarde` ;
6. demander le dossier de destination ;
7. exporter par nom logique vers un `.gshsave` ;
8. revalider l'enveloppe, le payload et le hash ;
9. afficher le chemin du fichier et `Envoyez ce fichier à Steven`.

L'utilitaire refuse l'export si le jeu tourne, si WGS n'est pas stable, si le monde disparaît ou change pendant la lecture, si la destination se trouve dans WGS, ou si la validation finale échoue. Un fichier partiel est supprimé après échec.

## 11. Promotion administrative d'une autre sauvegarde

Le mini-exporteur produit le fichier ; il ne modifie pas le hub. Le serveur doit fournir une commande administrative locale distincte, par exemple :

`world replace <world-id> <fichier.gshsave> <justification>`

Cette commande :

1. refuse toute session active sur le monde ;
2. valide l'artefact et la topologie des joueurs sans écrire dans WGS ;
3. publie l'objet de manière immuable ;
4. crée une nouvelle version candidate ;
5. protège explicitement la version principale précédente ;
6. bascule le pointeur courant dans la même transaction SQLite ;
7. conserve un audit avec l'auteur, la justification et les deux versions ;
8. laisse la commande `world restore` disponible pour le retour arrière.

La promotion est impossible si un joueur destiné à devenir hôte n'apparaît pas exactement une fois dans la sauvegarde. L'opération ne donne aucun droit administratif à Bob : il transmet uniquement le `.gshsave` à Steven.

## 12. Gestion des erreurs

Les erreurs sont classées en trois niveaux :

- **attente normale** : serveur momentanément inaccessible, autre hôte actif, synchronisation en cours ; l'application réessaie sans alarme ;
- **action simple** : jeu encore ouvert, placeholder manquant, application Xbox absente ; une consigne unique est affichée ;
- **arrêt de sûreté** : hash inattendu, monde protégé modifié, sessions locales concurrentes, import ambigu ou reprise non démontrable ; aucune écriture automatique n'est tentée et les détails sont conservés pour le diagnostic.

Les messages utilisateurs ne montrent pas les exceptions, GUID ou chemins WGS. Un panneau secondaire permet de copier un rapport technique et son identifiant de corrélation.

## 13. Validation

### 13.1 Tests automatisés

- table complète de dérivation des états d'accueil ;
- sérialisation et compatibilité des nouveaux contrats API ;
- exposition sûre de l'identité de l'hôte ;
- polling, annulation, backoff et perte de connexion ;
- découverte et activation AUMID avec doublures de test ;
- blocage des acquisitions et écritures lorsque le jeu local est actif hors session ;
- verrou commun entre mise à jour et transfert ;
- signature, hash, activation, rollback et version minimale ;
- export par nom logique, homonymes, mutation pendant lecture et nettoyage des fichiers partiels ;
- promotion administrative atomique, session active, protection de l'ancienne version et restauration ;
- maintien de tous les tests d'adaptateur et d'orchestrateur existants.

### 13.2 Validation visuelle et accessible

- comparaison de l'implémentation avec le concept accepté à la même taille ;
- contrôle des débordements aux mises à l'échelle Windows 100 %, 125 %, 150 % et 200 % ;
- navigation clavier complète, focus visible et ordre logique ;
- contraste, lecteurs d'écran et annonces de progression ;
- aucune information technique sur le parcours quotidien.

### 13.3 Validation réelle

Avant passage du canal `pilot` au canal `stable` :

1. cycle complet `Steven → Bob → Steven` ;
2. redémarrage Windows pendant une session récupérable ;
3. coupure contrôlée pendant l'import ;
4. échec de capture suivi d'une reprise ;
5. mise à jour détectée mais différée pendant le jeu ;
6. mise à jour réussie au repos puis test de rollback ;
7. export de la sauvegarde actuelle de Bob avec le mini-programme ;
8. validation et promotion administrative de cet artefact ;
9. prise en main de la nouvelle version sur les deux PC ;
10. observation de l'état Xbox Cloud après fermeture.

## 14. Découpage et ordre de réalisation

Ce document est la conception d'ensemble. Sa mise en œuvre est trop large pour un seul lot et doit être découpée en trois plans indépendants, livrés et vérifiés dans cet ordre :

1. **Sauvegarde actuelle de Bob** : mini-exporteur, puis commande de promotion administrative sûre. Ce lot rend immédiatement possible l'adoption du monde réellement joué aujourd'hui sans complexifier le client principal.
2. **Expérience quotidienne** : contrat de présence et identité de l'hôte, dérivation d'état, refonte WPF, lancement Xbox et détection contextuelle.
3. **Distribution durable** : installateur unique, manifeste signé, canaux, activation différée et rollback.

La campagne réelle `Steven → Bob → Steven` clôt le deuxième lot. Le canal `pilot` valide ensuite le troisième lot avant toute ouverture de `stable` aux autres joueurs.

La refonte visuelle ne doit jamais devancer les protections fonctionnelles : chaque nouvel état visible repose sur un signal serveur ou local explicite et couvert par des tests.
