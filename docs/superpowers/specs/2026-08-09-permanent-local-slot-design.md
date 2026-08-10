# GameSave Hub V1 — slot local permanent

**Statut :** conception validée avec l’utilisateur le 9 août 2026

**Périmètre :** client Windows 11 x64, The Planet Crafter Xbox/Game Pass, un monde partagé principal et un slot local géré par PC

## 1. Contexte

Le premier pilote Lot 2 a validé la chaîne complète : acquisition du monde, création d’un emplacement WGS, import de la sauvegarde principale, détection du jeu, capture, publication et libération du verrou. Il a aussi révélé une erreur de conception du parcours : chaque prise en main créait un nouveau monde local nommé `GSH-SHLAGS-RETURN`. Après plusieurs essais, le menu du jeu contenait plusieurs sauvegardes homonymes qu’il était impossible de distinguer.

La création d’un slot WGS est une contrainte d’initialisation propre à chaque PC, pas une étape du parcours quotidien. La V1 doit donc créer un emplacement géré une seule fois, mémoriser son identité logique et le réutiliser pour toutes les prises en main suivantes.

## 2. Décision produit

Chaque PC enrôlé possède au maximum un slot local géré nommé :

`GSH-MONDE-PARTAGE`

Le nom visible aide le joueur à reconnaître la sauvegarde dans The Planet Crafter. L’identité de sécurité reste le nom logique WGS découvert lors de la création, par exemple `Standard-5.json`. Le client ne choisit jamais un slot uniquement à partir de son nom visible.

La manipulation spéciale dans le jeu n’a lieu que lorsque ce PC ne possède encore aucun slot géré valide. Une fois le slot enregistré, une prise en main ordinaire ne crée aucun monde supplémentaire et ne demande aucun nom à recopier.

## 3. Parcours utilisateur

### 3.1 Première configuration sur un PC

Quand le PC est enrôlé mais qu’aucun slot permanent n’est enregistré, l’accueil remplace `Prendre la main` par `Configurer ce PC`.

Le parcours explique qu’il s’agit d’une opération unique :

1. GameSave Hub acquiert et prépare le monde sans écrire dans un slot ambigu.
2. L’écran affiche `GSH-MONDE-PARTAGE` dans un contrôle sélectionnable avec un bouton `Copier le nom`.
3. Le bouton de copie place exactement ce nom dans le presse-papiers et affiche brièvement `Copié` sans déplacer le focus.
4. `Lancer The Planet Crafter` ouvre la version Xbox.
5. Le joueur crée un nouveau monde portant exactement ce nom, entre une fois dans le monde, sauvegarde puis ferme complètement le jeu.
6. Le service attend la stabilité WGS, exige un unique nouveau candidat cohérent et mémorise son nom logique.
7. L’artefact du Hub est préparé pour le joueur puis importé dans ce slot avec snapshot et validation.
8. L’écran devient `Votre PC est configuré` et propose le lancement final pour jouer.

Cette première configuration comporte donc deux lancements imposés par WGS : un lancement exceptionnel pour créer le slot, puis le lancement normal du monde importé. L’interface les annonce explicitement comme `Configuration unique — étape 1 sur 2` et `Configuration unique — étape 2 sur 2`.

### 3.2 Prise en main quotidienne

Quand un slot permanent valide est enregistré :

1. Le joueur clique sur `Prendre la main`.
2. Le client acquiert le verrou et télécharge la dernière version.
3. Il vérifie le slot enregistré, crée les snapshots nécessaires et remplace son contenu de manière sûre et idempotente.
4. L’écran affiche `Tout est prêt pour jouer` puis `Lancer The Planet Crafter`.
5. Le joueur charge toujours `GSH-MONDE-PARTAGE`.
6. Le service détecte le démarrage puis la fermeture du jeu, attend la stabilité, capture ce même nom logique et publie la nouvelle version.

Le parcours quotidien ne crée aucun placeholder, n’affiche aucun code à recopier et ne demande aucun second lancement.

## 4. Identité et persistance du slot

Le profil local persistant ajoute un enregistrement de slot contenant au minimum :

- le schéma de l’enregistrement ;
- le nom logique WGS ;
- le nom visible attendu `GSH-MONDE-PARTAGE` ;
- l’identité du package Xbox ;
- l’identité du joueur local ;
- la date de liaison et la dernière date de validation ;
- la dernière topologie de joueurs validée, utilisée uniquement comme information de diagnostic.

Le nom logique est la clé d’accès. Le nom visible, le package Xbox et l’identité locale servent de preuves de cohérence. Le contenu du monde, son hash, sa progression et sa topologie de joueurs peuvent changer normalement après une partie ou une promotion administrative ; ils ne peuvent donc pas identifier durablement le slot. La topologie courante est validée à chaque import et capture selon les règles existantes, sans être comparée à une valeur historique figée.

L’enregistrement est mis à jour atomiquement seulement après la découverte non ambiguë et la validation finale du premier import. Une session interrompue avant ce point ne laisse jamais un slot déclaré prêt.

## 5. Découverte et réutilisation

Avant chaque écriture, le client inspecte WGS lorsque le jeu est fermé et stable.

- Si le nom logique enregistré existe exactement une fois et son nom visible correspond, le slot peut être réutilisé.
- Si aucun slot n’est enregistré et qu’aucun monde `GSH-MONDE-PARTAGE` n’existe, le parcours de première configuration est proposé.
- Si aucun slot n’est enregistré mais qu’un unique monde `GSH-MONDE-PARTAGE` existe, l’application propose un rattachement contrôlé. Le rattachement exige le jeu fermé, WGS stable, un snapshot, un candidat logique unique, le pseudo local présent exactement une fois comme hôte `0`, puis une confirmation explicite de l’utilisateur ; il n’est jamais silencieux.
- Si le nom logique enregistré a disparu, a été renommé, pointe vers une topologie incohérente ou si plusieurs candidats sont présents, le client entre en arrêt de sûreté.
- Un homonyme créé par l’utilisateur n’est jamais choisi à la place du nom logique enregistré.

La réutilisation remplace le contenu du slot lié ; elle ne crée pas de nouveau nom logique. Les protections existantes restent obligatoires : jeu fermé, WGS stable, baseline, snapshot avant import, validation juste avant écriture, validation après écriture et reprise idempotente.

## 6. Réparation

La réparation est distincte du parcours quotidien et ne supprime jamais automatiquement une sauvegarde.

Elle couvre :

- slot enregistré absent ;
- unique slot visible existant mais profil local perdu ;
- homonymes visibles ;
- identité logique présente avec un nom visible inattendu ;
- interruption pendant la première configuration.

L’application décrit le problème en termes humains, conserve les détails techniques dans les diagnostics et interdit toute acquisition ou écriture tant que l’identité du slot n’est pas démontrée. Une suppression ou un nettoyage reste une action explicite réalisée dans l’interface du jeu après snapshot et accord de l’utilisateur.

## 7. États d’interface

Les nouveaux états testables sont :

| Contexte | Titre | Action principale |
|---|---|---|
| Aucun slot lié, monde libre | `Configurons ce PC` | `Configurer ce PC` |
| Création du slot requise | `Configuration unique — étape 1 sur 2` | `Copier le nom`, puis `Lancer The Planet Crafter` |
| Slot découvert, import en cours | `Installation du monde partagé…` | aucune |
| Premier import terminé | `Configuration unique — étape 2 sur 2` | `Lancer The Planet Crafter` |
| Slot lié et monde libre | `Le monde est prêt` | `Prendre la main` |
| Import quotidien terminé | `Tout est prêt pour jouer` | `Lancer The Planet Crafter` |
| Slot incohérent | `Le slot du monde doit être vérifié` | `Ouvrir l’assistance` |

Les GUID, noms logiques, chemins WGS, hashes et étapes internes restent absents de l’accueil.

## 8. Erreurs et reprise

Les opérations de création, rattachement et réutilisation passent par le même verrou local de transition que les transferts existants. Elles refusent les sessions locales concurrentes et tout jeu actif hors du cycle attendu.

- Une mutation WGS pendant l’observation entraîne une attente ou une interruption récupérable, jamais une écriture forcée.
- Une ambiguïté d’identité entraîne `ManualReview` et aucune sélection implicite.
- Une interruption après snapshot mais avant validation finale reprend depuis le checkpoint durable.
- Une interruption après import réussi reconnaît l’état déjà appliqué et n’écrit pas une seconde fois.
- Une capture après fermeture exporte exclusivement le nom logique lié à la session.

Le profil du slot et le checkpoint de transfert ne se contredisent jamais : la session conserve une copie de l’identité logique utilisée, et le profil permanent n’est modifiable qu’en l’absence de session active.

## 9. Compatibilité du PC pilote

Après le nettoyage validé du 9 août 2026, le PC de Steven contient un seul slot de test pertinent :

- nom logique : `Standard-5.json` ;
- nom visible actuel : `GSH-SHLAGS-RETURN`.

La migration pilote doit rattacher explicitement ce nom logique existant, puis renommer son contenu en `GSH-MONDE-PARTAGE` lors du prochain import sûr. Elle ne recrée pas de placeholder et ne modifie pas `Shlags1` ni `Standard-1`.

Les autres PC n’ayant jamais utilisé GameSave Hub suivent le parcours de première configuration. Les clients déjà utilisés mais ambigus sont bloqués et réparés explicitement.

## 10. Tests et critères d’acceptation

Les tests automatisés couvrent au minimum :

- copie exacte de `GSH-MONDE-PARTAGE` et état visuel `Copié` ;
- première configuration sans slot ;
- découverte d’un unique nouveau slot par nom logique ;
- refus de zéro ou plusieurs candidats ;
- persistance atomique après import validé seulement ;
- réutilisation du même nom logique sur deux prises en main successives ;
- absence de nouveau monde WGS lors d’une prise en main quotidienne ;
- slot manquant, renommé, homonyme ou topologiquement invalide ;
- profil local perdu avec candidat unique ;
- interruption avant et après l’import, reprise et idempotence ;
- capture finale du seul slot lié ;
- migration contrôlée de `Standard-5.json` sur le PC pilote ;
- présentation claire des deux étapes initiales et du parcours quotidien à un lancement.

L’acceptation réelle exige :

1. snapshot WGS préalable ;
2. rattachement du slot existant sur le PC de Steven ;
3. prise en main sans création de `Standard-6.json` ;
4. apparition d’un seul `GSH-MONDE-PARTAGE` dans le menu ;
5. chargement, sauvegarde et fermeture ;
6. publication réussie et monde de nouveau disponible ;
7. seconde prise en main confirmant la réutilisation du même nom logique ;
8. répétition du parcours de première configuration sur un PC vierge avant diffusion à Bob.

## 11. Compatibilité avec le Lot 3 — mise à jour et rollback

L’identité du slot permanent est stockée séparément dans :

`%ProgramData%\GameSaveHub\managed-slot.json`

Elle n’est pas ajoutée à `client-state.json`. Une ancienne version du client désérialise uniquement les champs qu’elle connaît puis réécrit ce fichier lors d’un changement d’état ; elle pourrait donc supprimer silencieusement des champs de slot ajoutés au même document. Le fichier séparé possède son propre schéma, ses écritures atomiques et reste conservé avec les autres données `ProgramData` lors d’une mise à jour ou désinstallation.

Le verrou de sûreté du futur updater couvre les opérations suivantes en plus des sessions déjà prévues :

- première configuration du slot ;
- rattachement d’un slot existant ;
- migration du nom visible historique ;
- réparation ou revue manuelle du slot ;
- toute inspection, baseline, écriture, réconciliation ou validation WGS associée.

Une mise à jour ne peut être activée que lorsque le jeu est fermé, qu’aucune session locale n’est active, qu’aucune opération de slot ne détient le verrou de transition et que les checkpoints du transfert et du slot sont durables. Le contrôle de santé de l’updater est strictement en lecture seule et ne déclenche jamais de migration WGS.

Après déploiement du slot permanent, l’API utilise `ClientCompatibility.MinimumAcquireVersion` pour empêcher un ancien client de prendre la main et de recréer des placeholders. Le déploiement reste additif : l’API compatible est installée d’abord avec la version minimale non contraignante, les clients compatibles sont installés et vérifiés, puis la version minimale est relevée explicitement. Un client trop ancien peut toujours lire l’état du monde mais reçoit `client_update_required` à l’acquisition.

Le premier client comprenant le slot permanent constitue le socle installé manuellement avant l’activation de la chaîne Lot 3. Après toute migration réelle du slot, un rollback automatique ne peut cibler qu’une version qui comprend `managed-slot.json` et respecte le même verrou d’acquisition. Les versions antérieures restent des artefacts historiques ou de récupération administrative ; elles ne sont pas des cibles d’activation automatique.

## 12. Hors périmètre

Cette correction n’ajoute pas :

- de suppression automatique des anciens mondes ;
- de sélection quotidienne entre plusieurs slots ;
- de modification directe ou manuelle des fichiers WGS ;
- de support Steam ou console ;
- de changement au modèle serveur d’un monde partagé principal.

Le déploiement du client corrigé, toute nouvelle écriture WGS réelle et toute intervention NAS restent soumis aux portes d’approbation existantes.
