# 07 — Revue critique du code

Constats issus de la lecture du code source, pas de la documentation. Chaque point indique ce qui a été vérifié.

## Ce que le code fait mieux que ce que la doc laisse croire

Trois soupçons se sont révélés infondés après vérification — autant le dire, ça situe le niveau de rigueur réel :

- **Rafraîchissement du jeton JWT.** Le jeton dure 15 minutes et une session de jeu dure des heures. `AuthenticatedTransferServerClient.GetAccessTokenAsync` réauthentifie automatiquement dès qu'il reste moins d'une minute, sous `SemaphoreSlim`. Le heartbeat de 30 s survit donc à une partie longue.
- **Clé d'idempotence auto-générée.** `EnsureMutationIdempotency` ajoute un GUID aléatoire à chaque POST/PUT, ce qui rendrait le rejeu inopérant — sauf que la seule route qui consulte réellement la table `Idempotency` est `acquire`, et elle reçoit toujours une clé explicite persistée avant l'appel réseau. Les autres routes sont idempotentes par état.
- **Préflight contourné depuis l'IHM.** L'IHM peut envoyer `transfer-start` sans préflight à jour, mais `PipeServerWorker.StartTransferAsync` rejoue `RunPreflightAsync` avant de déléguer à l'orchestrateur. La sûreté tient.

Le double verrou est réellement appliqué : `EnableWgsTransfer` est testé en premier dans `StartTransferAsync`, et `AllowHostTransfer` bloque `POST /worlds/{id}/acquire`, c'est-à-dire le point d'entrée unique de tout le flux serveur.

---

## 🔴 1. L'IHM Phase 3 ne peut pas mener un transfert à son terme

**Vérifié :** comparaison des `Click=` de `MainWindow.xaml` entre les archives Phase 2 r3 et Phase 3, et du `switch` de `PipeServerWorker.DispatchAsync`.

Le service expose six commandes de transfert :

```text
transfer-start
transfer-placeholder-ready
transfer-play-started
transfer-play-complete
transfer-resume
transfer-abort
```

L'application Phase 3 n'en câble **qu'une seule** : `transfer-start`. Les cinq autres n'ont plus aucun bouton.

| Bouton | Phase 2 r3 | Phase 3 |
|---|---|---|
| `TransferStart_Click` | ✅ | ✅ |
| `PlaceholderReady_Click` | ✅ | ❌ |
| `PlayStarted_Click` | ✅ | ❌ |
| `PlayComplete_Click` | ✅ | ❌ |
| `TransferResume_Click` | ✅ | ❌ |
| `TransferAbort_Click` | ✅ | ❌ |

La Phase 3 a reconstruit l'IHM autour de l'enrôlement, du catalogue et du préflight, et a **supprimé le panneau « Orchestrateur pilote »** de la Phase 2 sans le réintégrer.

Conséquence : même avec les deux verrous ouverts, un joueur ne peut pas terminer un transfert depuis l'application. La machine d'états s'arrête à `AwaitingPlaceholder` et il n'existe aucun moyen de reprendre une session `Interrupted` ni d'abandonner avant `import-starting`.

**C'est le blocage numéro un pour une v1 fonctionnelle**, devant même le problème Portainer.

## 🔴 2. `async void` + filtres d'exception trop étroits = crash de l'application

**Vérifié :** `MainWindow.xaml.cs`, tous les gestionnaires.

`TransferStart_Click` (ligne 194) n'a **aucun** `try`/`catch`. Le service arrêté, le pipe fermé ou une réponse malformée produisent une exception dans un `async void` : pas de gestionnaire, l'application se ferme sans message.

Les autres gestionnaires filtrent uniquement `IOException`, `TimeoutException` et `HttpRequestException`. Tout le reste s'échappe :

- `JsonException` si la réponse du pipe est tronquée ;
- `FormatException` / `InvalidOperationException` dans `ApplyPreview`, qui appelle `GetInt64()` sur `worldSeed` et `GetInt32()` sur `id` sans vérifier le type sous-jacent ;
- `UnauthorizedAccessException` si les ACL du pipe ne correspondent pas.

Pour une application destinée à des joueurs non techniciens, une fermeture silencieuse pendant une opération de transfert est le pire comportement possible.

**Correctif :** un `try`/`catch (Exception)` par gestionnaire plus un `DispatcherUnhandledException` global qui journalise et affiche, au lieu de terminer le processus.

## 🟠 3. L'IHM affiche un préflight périmé

**Vérifié :** `MainWindow.xaml` n'a aucun `SelectionChanged` sur `WorldComboBox`, et `MainWindow.xaml.cs` ne réinitialise `_preflightCompatible` que dans `ResetPreview()`, appelé uniquement par `LoadWorldsAsync`.

Séquence : préflight sur le monde A → « compatible » en vert, bouton activé → l'utilisateur change de monde dans la liste → **le vert et le bouton restent**. La liste des joueurs et le hash affichés sont toujours ceux du monde A.

La sûreté tient (point vérifié plus haut : le service rejoue le préflight), mais l'IHM affirme quelque chose de faux et le refus qui suivra sera incompréhensible.

**Correctif :** brancher `SelectionChanged` sur `ResetPreview()`. Trois lignes.

## 🟠 4. `SetBusy` gèle toute la fenêtre

`private void SetBusy(bool busy) => IsEnabled = !busy;`

`RefreshAsync` enchaîne trois allers-retours pipe (`status`, `server-health`, `transfer-active`), dont un qui interroge le NAS. Avec un NAS injoignable, la fenêtre entière est inerte pendant le timeout, sans indicateur d'activité ni annulation possible. Le comportement est indistinguable d'un plantage.

**Correctif :** indicateur de progression + désactivation ciblée des seuls boutons concernés.

## 🟡 5. Écart doc/code sur le ratio de compression

`ARTIFACT-FORMAT.md` annonçait un refus au-delà d'un ratio de 10 ; `ArtifactEnvelopeValidator.MaximumCompressionRatio` vaut **100**. La documentation a été corrigée pour refléter le code.

À trancher : l'artefact est censé être un ZIP **non compressé**, donc de ratio ≈ 1. Une limite à 100 est très permissive pour une protection anti-zip-bomb. Descendre à 10 serait plus cohérent avec l'intention — mais c'est un changement de comportement de validation, à faire délibérément et avec un test dédié, pas en passant.

---

## Architecture — ce que je remettrais en question

### Construire l'image sur le NAS est le mauvais choix

C'est la cause structurelle du blocage actuel. Le flux « je génère un `.tar` sur mon PC, je l'upload dans Portainer, Portainer construit » cumule les fragilités :

- l'upload doit traverser l'interface web (c'est ce qui a échoué avec 501 Mo) ;
- les logs de build sont ceux que Portainer veut bien exposer — en l'occurrence aucun ;
- le NAS doit tirer `mcr.microsoft.com/dotnet/sdk:10.0-alpine` et restaurer tous les paquets NuGet, sur une machine dont ce n'est pas le métier ;
- rien n'est reproductible ni tracé.

Le dépôt GitHub existe désormais. Un workflow GitHub Actions qui construit l'image et la publie sur **GHCR** supprime tout cela d'un coup : logs complets et lisibles, build reproductible, image immuable taguée, et le NAS n'a plus qu'à faire un `pull`. Le NAS redevient ce qu'il doit être — un hôte d'exécution, pas une chaîne de compilation.

Solution intermédiaire immédiate, sans CI : construire l'image sur le PC avec Docker Desktop, `docker save` vers un `.tar`, puis **Portainer → Images → Import**. C'est un endpoint différent de « Build image » : cela contourne complètement la fonction défaillante.

### Autres points d'infrastructure

- **`--providers.file.watch=false`** dans Traefik : toute modification de `routes.yml` exige un redémarrage du conteneur. Volontaire pour la stabilité, mais à connaître avant de chercher pourquoi une route ne bouge pas.
- **Aucune limite de ressources** (`mem_limit`, `cpus`) sur les quatre services, sur un NAS qui héberge aussi Home Assistant. Une fuite mémoire dans l'API peut affecter la domotique.
- **Sauvegarde manuelle uniquement.** `DEPLOYMENT.md` documente quoi sauvegarder, mais rien ne l'exécute. Pour un système dont la raison d'être est de ne pas perdre de sauvegardes, un backup automatisé de `data/` avec checkpoint WAL est un prérequis de v1, pas une amélioration.
- **`buffering` Traefik avec `maxRequestBodyBytes` à 256 Mio** alors que `Storage__MaxChunkBytes` vaut 4 Mio. Aucun risque, mais la limite du proxy pourrait être alignée sur celle de l'application pour rejeter tôt.

### Un point de conception à valider avec l'usage

Le modèle « un seul verrou par monde, jamais libéré automatiquement après `import-starting` » est la bonne décision de sûreté. Mais concrètement : si un joueur lance un transfert puis part en vacances, le monde reste verrouillé jusqu'à une intervention d'administration en ligne de commande dans un conteneur Docker.

Pour quatre amis, c'est acceptable à condition que l'administrateur soit joignable. Il faudrait au minimum que l'IHM affiche **qui** détient le verrou et **depuis quand** — l'information existe côté serveur (`Sessions.DeviceId`, `CreatedAtUtc`) mais n'est pas exposée par `GET /worlds`.
