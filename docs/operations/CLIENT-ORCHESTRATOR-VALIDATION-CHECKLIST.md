# Checklist de validation — GameSave Hub V1

**Dernière mise à jour :** 9 août 2026

**Branche de validation :** `codex/v1-lot2-contextual-app`

**État global :** Lot 1 validé localement ; socle Lot 2 implémenté et piloté ; correction du slot permanent spécifiée mais pas encore implémentée ; Lot 3 non commencé.

Cette checklist ne marque comme validé que ce qui a été observé ou vérifié. Le plan d’exécution du slot permanent est `docs/superpowers/plans/2026-08-09-permanent-local-slot-implementation.md`.

## Validé

### Lot 1 — export et remplacement administratif

- [x] Mini-exporteur portable construit et testé sur le PC de Steven.
- [x] Export réel produit et validé : `artifacts/local-smoke-export/20260809T131027Z-1ce9e64c2eb448b4939e5d48b5e6daf1.gshsave`.
- [x] Import local immuable, validation de topologie, remplacement transactionnel, audit et restauration couverts par les tests.
- [x] Documentation de transfert Bob → Steven et stack Portainer d’administration préparées sans déploiement NAS.
- [x] Aucune promotion réelle ni modification NAS effectuée sans accord.

### Socle Lot 2 — code et contrats

- [x] Solution restaurée, compilée et testée le 9 août 2026 : **202 tests réussis, 0 échec**.
- [x] `GameSaveHub.Client.Orchestration` intégré à la solution.
- [x] Authentification challenge ECDSA + JWT conservée ; `RegisteredUserSid` reste obligatoire.
- [x] Le service résout le profil joueur par SID et n’utilise pas le profil LocalSystem pour WGS.
- [x] Sessions et checkpoints persistés sous `ProgramData`, avec reprise d’acquisition et d’upload idempotente.
- [x] Rejeu du commit serveur connu après crash sans recréer un upload.
- [x] Import protégé par jeu fermé, stabilité WGS, baseline, snapshot, validation et réconciliation.
- [x] Présence serveur, identité du joueur, activité récente et compatibilité JSON additive implémentées.
- [x] Accueil contextuel, worker de cycle de jeu et reprise après redémarrage implémentés.
- [x] Lancement Xbox/Game Pass réel validé depuis l’application WPF ; `CanLaunchGame=true`.
- [x] Application WPF refondue avec accueil orienté joueur, progression et diagnostics séparés.

### Pilote réel Steven — acquis réutilisables

- [x] API/NAS Lot 2 déployée et saine avec migrations appliquées, après accord utilisateur.
- [x] Client et service Windows installés et opérationnels sur le PC de Steven.
- [x] Acquisition, téléchargement, préparation, création d’un placeholder, import, détection du jeu, capture, upload, publication et libération du monde observés sur un cycle pilote.
- [x] Le contexte s’adapte lorsqu’un autre joueur héberge : l’action attendue est de lancer Xbox pour rejoindre, sans acquisition locale.
- [x] Snapshot WGS complet créé avant nettoyage : `artifacts/pre-cleanup-wgs-20260809T171531Z/20260809T151533Z-9cc8ba99af2b4cf5b44a05663e6d567e`.
- [x] Snapshot validé : 11 fichiers WGS ; SHA-256 du manifeste `58f4698c81cf1686bac7fa2bd4e6012234b87775a8063357fb4a1853d105ef4a`.
- [x] Nettoyage manuel validé : il reste `Standard-1`, `Shlags1` et un seul monde de test `GSH-SHLAGS-RETURN` lié à `Standard-5.json`.
- [x] Nom produit du slot permanent validé : `GSH-MONDE-PARTAGE`.
- [x] Spécification du slot permanent et contraintes de compatibilité Lot 3 rédigées.

## Implémenté mais à valider réellement

- [ ] Charger réellement le monde importé, modifier un élément identifiable, sauvegarder, fermer et vérifier la progression publiée de bout en bout.
- [ ] Vérifier un redémarrage Windows pendant une session `InGame`, puis la reprise de capture après stabilité.
- [ ] Vérifier une coupure contrôlée pendant l’import et la réconciliation sans nouvelle écriture ambiguë.
- [ ] Vérifier les états contextuels : jeu lancé hors Hub, serveur indisponible puis rétabli, hôte distant en préparation et en jeu, reprise manuelle après incident.
- [ ] Vérifier l’interface à 1440×1024 et aux échelles Windows 100 %, 125 %, 150 % et 200 %.
- [ ] Vérifier clavier seul, focus visible, noms lecteur d’écran, retour `Copié`, absence de débordement et absence d’identifiants techniques sur l’accueil.

## À implémenter — slot permanent

- [ ] Persister l’identité du slot dans `%ProgramData%\GameSaveHub\managed-slot.json`, séparément de `client-state.json`, avec schéma et écriture atomique.
- [ ] Résoudre sans ambiguïté les états : absent, création requise, rattachement historique, prêt, manquant, homonyme et revue manuelle.
- [ ] Préparer l’artefact hôte avec le nom visible fixe `GSH-MONDE-PARTAGE` dans le payload et le manifeste.
- [ ] Ajouter une voie dédiée de remplacement du slot lié, sans assouplir la sécurité du mécanisme placeholder existant.
- [ ] Rattacher explicitement le slot pilote `Standard-5.json` sans modifier ses octets au moment du rattachement.
- [ ] Garantir qu’une prise en main quotidienne ne crée aucun nouveau `Standard-X`.
- [ ] Capturer et publier exclusivement le nom logique lié à la session.
- [ ] Ajouter les états UI de configuration unique en deux étapes, le bouton `Copier le nom`, le rattachement historique et l’arrêt de sûreté.
- [ ] Ajouter `maintenance-status`, strictement en lecture seule, incluant jeu fermé, aucune session, transition inactive et checkpoint durable.
- [ ] Ajouter `X-GameSaveHub-Client-Version` et la barrière API additive `ClientCompatibility.MinimumAcquireVersion`.
- [ ] Construire et vérifier le paquet manuel `0.4.0-pilot`, futur plus ancien rollback automatique autorisé du Lot 3.
- [ ] Ajouter le runbook pilote du slot permanent et actualiser cette checklist après chaque preuve réelle.

## Acceptation réelle du slot permanent

- [ ] Créer un nouveau snapshot WGS et une sauvegarde de `ProgramData` juste avant migration.
- [ ] Installer `0.4.0-pilot` sur Steven après approbation explicite.
- [ ] Rattacher l’unique `GSH-SHLAGS-RETURN`, puis le renommer en `GSH-MONDE-PARTAGE` uniquement lors de l’import sûr suivant.
- [ ] Première prise en main : même nom logique, aucun monde supplémentaire, vraie modification de jeu publiée, monde serveur de nouveau disponible.
- [ ] Deuxième prise en main : même nom logique réutilisé et ensemble des mondes WGS inchangé.
- [ ] Vérifier redémarrage, interruption d’import, reprise idempotente et `maintenance-status`.
- [ ] Confirmer qu’aucune suppression automatique n’a eu lieu et que `Shlags1` et `Standard-1` sont inchangés.

## Compatibilité Lot 3 à préserver

- [ ] L’updater bloque toute activation pendant configuration, rattachement, migration, réparation, inspection ou écriture du slot.
- [ ] Le contrôle de santé de l’updater ne déclenche aucune migration ou écriture WGS.
- [ ] `managed-slot.json` survit à la réinstallation et à la mise à jour ; il ne contient aucun secret.
- [ ] Déployer d’abord l’API additive avec version minimale non contraignante, puis les clients compatibles.
- [ ] Ne relever `MinimumAcquireVersion` qu’après validation du même paquet sur Steven et Bob.
- [ ] Après migration réelle, n’autoriser le rollback automatique que vers une version comprenant le slot permanent.
- [ ] Garder le jeu, toute session locale et tout checkpoint non durable comme motifs de report de mise à jour.

## Portes externes

- [ ] Bob est disponible pour recevoir et tester le mini-exporteur.
- [ ] Installer sur Bob exactement le même ZIP `0.4.0-pilot` et vérifier son SHA-256.
- [ ] Réaliser sa configuration unique avec un seul `GSH-MONDE-PARTAGE`.
- [ ] Réaliser le cycle réel `Steven → Bob → Steven` avec progression vérifiée à chaque publication.
- [ ] Vérifier le parcours de connexion pendant que Bob héberge, puis le parcours symétrique.
- [ ] Obtenir un accord explicite avant tout nouveau déploiement NAS/API, migration de production, écriture WGS de validation ou élévation de version minimale.
- [ ] Exécuter la revue finale, les tests complets et les sauvegardes finales.
- [ ] Obtenir la validation conjointe avant fusion vers `main` ; aucun push ou merge avant cet accord.

## Critère de clôture

Le Lot 2 n’est terminé que lorsque toutes les cases « Acceptation réelle du slot permanent » et les validations Bob sont cochées avec preuves, que le total automatisé reste au moins égal à 202 tests sans échec, et que l’utilisateur a validé le résultat. Le Lot 3 peut alors utiliser `0.4.0-pilot` comme socle manuel compatible, sans revenir à une version susceptible de recréer des placeholders.
