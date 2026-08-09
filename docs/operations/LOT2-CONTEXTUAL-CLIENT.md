# Client contextuel V1

Le client quotidien suppose un seul monde principal : l'unique monde du catalogue qui possède un artefact. Si aucun monde ou plusieurs mondes répondent à cette règle, l'accueil s'arrête en sûreté et n'en choisit jamais un implicitement.

## Accueil

La commande locale `home-context` rassemble en une réponse l'enrôlement et le pseudo, la santé serveur, le monde principal, la session serveur, la session locale et son dernier résultat, le processus du jeu et l'état de stabilité WGS. L'application la relit toutes les cinq secondes sans lancer deux requêtes simultanées.

L'accueil rend cet état via `HomeStatePresenter`. Il ne montre plus de GUID, hash, seed, URL, NAS ou feature gate. Son action principale est déterminée par le contexte : prendre la main, lancer The Planet Crafter depuis Xbox, reprendre une session interrompue ou ouvrir le diagnostic. Lorsqu'un autre joueur héberge déjà, l'application invite simplement à lancer le jeu et à rejoindre sa partie par le menu multijoueur habituel.

L'onboarding et les diagnostics restent derrière l'engrenage après l'enrôlement initial.

## Automatisation du jeu

Le service observe le processus Xbox toutes les deux secondes. Il marque automatiquement le début d'une partie lorsque le jeu apparaît, puis capture et publie après sa fermeture et la vérification de stabilité. Un placeholder n'est confirmé automatiquement qu'après avoir réellement observé un cycle démarrage puis fermeture du jeu.

Une session `InGame` retrouvée après redémarrage avec le jeu fermé reprend la capture. Plusieurs sessions locales, `Interrupted` et `ManualReview` interdisent toute transition automatique. La reprise manuelle reste disponible en cas d'incident.

Le lancement Xbox est exécuté uniquement par l'application WPF interactive avec l'AUMID `MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t!Game`. Le service Windows ne lance jamais une application interactive. Si l'activation échoue, l'accueil propose d'ouvrir l'application Xbox.

## Contrats serveur

`WorldStatusResponse` expose de manière additive la session active et la dernière activité. `AcquireWorldRequest.PlayerName` reste nullable pour lire les anciens clients. Lorsqu'il est présent, le serveur le compare au manifeste de la version courante et conserve le pseudo canonique dans la session puis dans la version publiée.

La migration `AddSessionPlayerName` ajoute une colonne nullable ; les anciennes sessions restent lisibles et utilisent une formulation neutre dans l'interface.

## Portes de déploiement

Cette branche ne déploie rien. Avant un pilote réel, il faut sauvegarder SQLite, appliquer explicitement la migration, publier l'API et le client compatibles, puis seulement décider d'ouvrir les feature gates. Aucun changement NAS ou Portainer n'est inclus dans la validation locale du Lot 2.
