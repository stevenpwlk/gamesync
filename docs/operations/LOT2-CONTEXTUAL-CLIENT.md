# Client contextuel V1

Le client quotidien suppose un seul monde principal : l'unique monde du catalogue qui possède un artefact. Si aucun monde ou plusieurs mondes répondent à cette règle, l'accueil s'arrête en sûreté et n'en choisit jamais un implicitement.

## Slot local permanent

Chaque PC enrôlé possède au maximum un slot WGS géré, visible sous le nom `GSH-MONDE-PARTAGE`. Son identité durable (le nom logique WGS découvert lors de la création) est persistée dans `%ProgramData%\GameSaveHub\managed-slot.json`, séparément de `client-state.json`, avec un schéma versionné et une écriture atomique. Le nom visible n'est jamais utilisé comme identifiant : deux imports de la même sauvegarde produisent des mondes homonymes, seul le nom logique fait foi.

`ManagedSlotResolver.Resolve` détermine sans I/O l'état du slot pour ce PC à partir du binding persistant (s'il existe) et de l'inventaire WGS courant :

| Statut | Signification |
|---|---|
| `Missing` | Aucun slot lié, aucun `GSH-MONDE-PARTAGE` visible : première configuration à proposer. |
| `Ready` | Slot lié et cohérent : prise en main quotidienne normale. |
| `LegacyCandidate` | Aucun slot lié, mais un unique monde historique existe : rattachement explicite à proposer. |
| `UnboundCandidate`, `RenamePending`, `BoundSlotMissing`, `BindingMismatch`, `InvalidTopology`, `Ambiguous` | États incohérents : arrêt de sûreté, aucune sélection implicite. |

`ManagedSlotCoordinator` (dans `GameSaveHub.Client.Orchestration`, pas dans le service — il ne dépend que des abstractions déjà testables) expose `GetStatusAsync` (lecture seule) et `BindExistingAsync`, qui rattache le seul candidat historique après réinspection WGS immédiatement avant l'écriture, sans jamais muter WGS ni agir silencieusement.

`TransferOrchestrator.StartAsync` prend un `TransferFlowKind` explicite :

- `InitialSlotSetup` — première configuration : garde le chemin baseline/placeholder existant, mais fixe le nom du placeholder à `GSH-MONDE-PARTAGE` au lieu d'un nom aléatoire, et enregistre le binding (`ManagedSlotBinding`) exactement une fois après un import validé — y compris en reprise après incident, si l'import a réussi mais que l'enregistrement du binding a été interrompu.
- `ManagedSlotReuse` — prise en main quotidienne : lit le binding existant, saute entièrement l'étape `AwaitingPlaceholder` et appelle directement les primitives dédiées (`CreateManagedSlotBaselineAsync` / `ReplaceManagedSlotAsync` / `ReconcileManagedSlotReplacementAsync`) au lieu du chemin générique d'import par placeholder.
- `LegacyPlaceholder` — valeur par défaut, uniquement pour la compatibilité de désérialisation des anciennes sessions ; le service ne la choisit plus jamais explicitement.

Le service (`PipeServerWorker`) choisit `InitialSlotSetup` ou `ManagedSlotReuse` à partir d'une résolution fraîche au moment de `transfer-start`, et refuse net (sans écriture) tout statut ambigu ou nécessitant réparation. La commande `managed-slot-bind-existing` déclenche `BindExistingAsync` ; `maintenance-status` (strictement en lecture seule, ne migre ni n'écrit jamais WGS) renvoie l'état de sûreté pour la future mise à jour à distance du Lot 3 : jeu fermé, aucune session active, transition inactive (`TransferTransitionGate.IsBusy`), checkpoint durable (`ITransferSessionStore.IsWriteInProgress`).

## Verrou de version minimale (API)

Le service envoie l'en-tête `X-GameSaveHub-Client-Version: 0.4.0` sur chaque requête authentifiée. L'API évalue `ClientCompatibilityPolicy.CanAcquire` contre `ClientCompatibility:MinimumAcquireVersion` (vide par défaut, donc non contraignant) avant toute mutation de session, et refuse avec `client_update_required` (HTTP 409) sinon. La barrière ne sera relevée qu'après validation du même paquet sur les deux PC pilotes (Steven et Bob).

## Accueil

La commande locale `home-context` rassemble en une réponse l'enrôlement et le pseudo, la santé serveur, le monde principal, la session serveur, la session locale et son dernier résultat, le processus du jeu, l'état de stabilité WGS et le statut du slot géré (`ManagedSlotStatus`, lu uniquement quand jeu fermé/WGS stable/aucune session active, via `ManagedSlotCoordinator.GetStatusAsync`). L'application la relit toutes les cinq secondes sans lancer deux requêtes simultanées.

L'accueil rend cet état via `HomeStatePresenter`. Il ne montre plus de GUID, hash, seed, URL, NAS ou feature gate — ni de nom logique WGS. Son action principale est déterminée par le contexte, dans cet ordre de priorité : enrôlement, disponibilité serveur, cohérence de sûreté, session locale active, session distante, jeu lancé hors Hub, puis seulement le statut du slot géré. Cet ordre garantit qu'un joueur non configuré peut toujours lancer le jeu pour rejoindre un hôte distant sans être bloqué par la configuration de son propre slot.

Sur le statut du slot (aucune session locale active) : `Missing` propose `Configurer ce PC` ; `Ready` garde le `Prendre la main` quotidien inchangé ; `LegacyCandidate` propose explicitement `Rattacher ce monde` (jamais automatique) ; tout autre état déclenche un arrêt de sûreté `Le slot du monde doit être vérifié`. Pendant une session `InitialSlotSetup` active, l'accueil affiche `Configuration unique — étape 1 sur 2` (avec le contrôle `Copier le nom` copiant exactement `GSH-MONDE-PARTAGE`), `Installation du monde partagé…`, puis `étape 2 sur 2` ; le parcours `ManagedSlotReuse` quotidien garde son texte habituel sans étapes ni copie.

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

Le paquet client `0.4.0-pilot` (`tools/build-integrated-phase3.ps1 -PilotTransfer`) est construit et vérifié localement (322 tests, garde-fous statiques Phase 3 et Lot 2) mais n'a encore été installé sur aucun PC réel. Voir [`PERMANENT-SLOT-PILOT.md`](PERMANENT-SLOT-PILOT.md) pour la procédure d'installation et de migration pilote, et la [checklist de validation](CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md) pour l'état réel courant.
