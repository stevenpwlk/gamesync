# Checklist de validation — Client Orchestrator

## Build

- .NET SDK 10.x détecté.
- Tous les hashes `SOURCE-SHA256SUMS.txt` valides.
- Restauration réussie.
- Solution complète compilée sans warning traité comme erreur.
- 63 cas de test unitaires attendus, 0 échec.
- Capacités adapter : prepare=true, import=true, launch=false.

## Vérifications de code

- Projet `GameSaveHub.Client.Orchestration` présent dans la solution.
- `TransferRecoveryWorker` enregistré dans le service.
- `AuthenticatedTransferServerClient` utilise challenge ECDSA + JWT.
- `RegisteredUserSid` reste obligatoire.
- le service résout `AppData\Local` du joueur via le SID et l'injecte dans `PlanetCrafterGamePassAdapter`; aucun accès au profil LocalSystem.
- `TransferRootPath` est sous ProgramData par défaut.
- `ProbeImportTargetAsync` et `ReconcilePortableImportAsync` sont exposés par l'adapter.
- `WaitForSaveStabilityAsync` est en lecture seule.
- reprise acquisition = même `Idempotency-Key`.
- reprise upload = chunks serveur comme source de vérité.
- crash après commit serveur mais avant checkpoint local = rejeu direct du `commit` connu, sans recréer un upload.
- crash pendant import = aucune réécriture WGS automatique.
- feature gate production toujours fermé.

## Ce que ce build ne valide pas encore

- installation/mise à jour réelle du service Windows sur les deux PC ;
- UX finale de sélection des mondes (le pilote utilise encore un GUID serveur) ;
- lancement automatique de Planet Crafter ;
- ouverture du feature gate serveur ;
- test end-to-end orchestré avec serveur/NAS et deux PC.
