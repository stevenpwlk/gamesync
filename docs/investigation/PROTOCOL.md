# Protocole d'investigation WGS — jalon go/no-go

## Règles absolues

1. Employer deux PC Windows 11, deux comptes Xbox et un monde créé exclusivement pour les essais.
2. Fermer le jeu avant chaque snapshot. Ne jamais synchroniser, remplacer ou restaurer l'ensemble d'un dossier WGS entre deux machines.
3. Conserver une capture validée avant chaque manipulation expérimentale.
4. Noter l'état Xbox Cloud, le joueur hôte, les inventaires, positions et progression avant et après chaque essai.
5. Arrêter immédiatement en cas de conflit cloud non expliqué ou de perte d'identité/progression.

## Séquence

1. Sur chaque PC, lancer `inventory --json` avant et après : création du monde, arrivée du second joueur, sauvegarde, fermeture et redémarrage.
2. Jeu fermé, créer et valider un snapshot; comparer les manifestes afin d'identifier les blobs modifiés et les métadonnées propres au PC.
3. Identifier expérimentalement le blob portable sans déduire sa fonction uniquement de son nom ou de sa taille.
   Les noms hexadécimaux et générations `container.*` sont éphémères : résoudre à chaque fois le fichier logique dans les métadonnées WGS courantes.
4. Concevoir ensuite une importation ciblée sur copie de test; ne jamais remplacer `containers.index`, un fichier `container.*` ou un dossier WGS complet par ceux d'un autre PC.
5. Tester le cloud connecté, déconnecté, après reboot et après conflit volontaire. Toute boîte de dialogue Xbox reste une intervention humaine documentée.
6. Réaliser trois cycles A → B → A complets avec contrôle des quatre dimensions : hôte, inventaires, positions, progression mondiale.

## Décision

Le go exige trois cycles reproductibles, aucune perte, aucune sélection cloud ambiguë et une liste exacte des seuls blobs portables. Sinon, consigner un no-go dans ce dossier et ne pas débloquer les capacités sensibles de l'adaptateur.

## Essai mono-PC hors ligne

Le script `tools/offline-restore-test.ps1` restaure le monde jetable depuis le snapshot de référence. Il doit être lancé seulement après désactivation de toutes les routes Wi-Fi, Ethernet et VPN. Il crée automatiquement un snapshot pré-restauration et un snapshot final validé. Internet ne doit être réactivé qu'après son message de réussite; le jeu et l'application Xbox restent ensuite fermés jusqu'à l'analyse du rapport.
