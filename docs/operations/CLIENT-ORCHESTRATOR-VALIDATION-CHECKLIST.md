# Checklist de validation — GameSave Hub V1

**Dernière mise à jour :** 10 août 2026

**Branche de validation :** `codex/v1-lot2-contextual-app`

**État global :** Lot 1 fusionné sur `main`. Lot 2 (slot local permanent) entièrement implémenté et testé automatiquement (322 tests, 0 échec) ; le paquet `0.4.0-pilot` est construit et son SHA-256 connu. Rien de tout cela n'a encore été validé sur un vrai PC/WGS : c'est l'objet des tâches 12 à 14, toutes des portes externes nécessitant l'accord explicite de l'utilisateur et, pour la dernière, la disponibilité de Bob.

Cette checklist ne marque comme validé que ce qui a été observé ou vérifié réellement. Le plan d'exécution du slot permanent est `docs/superpowers/plans/2026-08-09-permanent-local-slot-implementation.md`.

## Validé

### Lot 1 — export et remplacement administratif

- [x] Mini-exporteur portable construit et testé sur le PC de Steven.
- [x] Export réel produit et validé : `artifacts/local-smoke-export/20260809T131027Z-1ce9e64c2eb448b4939e5d48b5e6daf1.gshsave`.
- [x] Import local immuable, validation de topologie, remplacement transactionnel, audit et restauration couverts par les tests.
- [x] Documentation de transfert Bob → Steven et stack Portainer d'administration préparées sans déploiement NAS.
- [x] Aucune promotion réelle ni modification NAS effectuée sans accord.
- [x] Fusionné sur `main` par fast-forward (2026-08-10), 153/153 tests à ce moment-là.

### Socle Lot 2 — code et contrats

- [x] `GameSaveHub.Client.Orchestration` intégré à la solution.
- [x] Authentification challenge ECDSA + JWT conservée ; `RegisteredUserSid` reste obligatoire.
- [x] Le service résout le profil joueur par SID et n'utilise pas le profil LocalSystem pour WGS.
- [x] Sessions et checkpoints persistés sous `ProgramData`, avec reprise d'acquisition et d'upload idempotente.
- [x] Rejeu du commit serveur connu après crash sans recréer un upload.
- [x] Import protégé par jeu fermé, stabilité WGS, baseline, snapshot, validation et réconciliation.
- [x] Présence serveur, identité du joueur, activité récente et compatibilité JSON additive implémentées.
- [x] Accueil contextuel, worker de cycle de jeu et reprise après redémarrage implémentés.
- [x] Lancement Xbox/Game Pass réel validé depuis l'application WPF ; `CanLaunchGame=true`.
- [x] Application WPF refondue avec accueil orienté joueur, progression et diagnostics séparés.

### Pilote réel Steven — acquis réutilisables (avant le slot permanent)

- [x] API/NAS Lot 2 déployée et saine avec migrations appliquées, après accord utilisateur.
- [x] Client et service Windows installés et opérationnels sur le PC de Steven.
- [x] Acquisition, téléchargement, préparation, création d'un placeholder, import, détection du jeu, capture, upload, publication et libération du monde observés sur un cycle pilote.
- [x] Le contexte s'adapte lorsqu'un autre joueur héberge : l'action attendue est de lancer Xbox pour rejoindre, sans acquisition locale.
- [x] Snapshot WGS complet créé avant nettoyage : `artifacts/pre-cleanup-wgs-20260809T171531Z/20260809T151533Z-9cc8ba99af2b4cf5b44a05663e6d567e`.
- [x] Snapshot validé : 11 fichiers WGS ; SHA-256 du manifeste `58f4698c81cf1686bac7fa2bd4e6012234b87775a8063357fb4a1853d105ef4a`.
- [x] Nettoyage manuel validé : il reste `Standard-1`, `Shlags1` et un seul monde de test `GSH-SHLAGS-RETURN` lié à `Standard-5.json`.
- [x] Nom produit du slot permanent validé : `GSH-MONDE-PARTAGE`.

## Implémenté et testé automatiquement — à valider réellement

Toutes les lignes ci-dessous sont codées, testées unitairement (mondes/adaptateur/service simulés) et intégrées dans le paquet `0.4.0-pilot`, mais **aucune n'a encore touché un vrai PC ni un vrai stockage WGS**.

### Slot local permanent

- [ ] Identité du slot persistée dans `%ProgramData%\GameSaveHub\managed-slot.json`, séparément de `client-state.json`, schéma versionné, écriture atomique (`FileManagedSlotStore`).
- [ ] Résolution sans ambiguïté des états (absent, candidat non lié, candidat historique, prêt, slot manquant, incohérence de liaison, topologie invalide, ambigu) via `ManagedSlotResolver`.
- [ ] Préparation de l'artefact hôte avec le nom visible fixe `GSH-MONDE-PARTAGE` dans le payload et le manifeste.
- [ ] Remplacement sûr du slot lié (`ReplaceManagedSlotAsync` / `ReconcileManagedSlotReplacementAsync`) : baseline, snapshot avant écriture, écriture atomique, validation après écriture, restauration sur échec — commit `07ad10d`, 77 tests dédiés à l'adaptateur, 274 tests au total à ce stade.
- [ ] Sessions de transfert distinguant configuration initiale et réutilisation (`TransferFlowKind`), compatibilité JSON additive avec les anciennes sessions, liaison du binding écrite une seule fois après import validé (y compris après reprise sur incident) — commit `c44d86d`, 281 tests.
- [ ] Rattachement explicite et jamais silencieux d'un slot historique unique (`ManagedSlotCoordinator.BindExistingAsync`), statut de sûreté `maintenance-status` strictement en lecture seule, commandes pipe `managed-slot-bind-existing` et `maintenance-status` — commit `a5fc82a`, 289 tests.
- [ ] Accueil contextuel complet : `Configurons ce PC`, configuration unique étape 1/2 avec bouton `Copier le nom` (texte exact `GSH-MONDE-PARTAGE`) et étape 2/2, rattachement historique, arrêt de sûreté générique pour tout état incohérent — commit `b90efc4`, 309 tests. **Rendu WPF non vérifié visuellement dans cette session** (pas d'affichage disponible) ; seule la logique de présentation est testée.
- [ ] Barrière de version client (`X-GameSaveHub-Client-Version`, `ClientCompatibility.MinimumAcquireVersion`, refus `client_update_required` avant toute mutation) — commit `115d8ec`, 322 tests. Valeur déployée par défaut : vide, non contraignante.
- [ ] Paquet manuel `0.4.0-pilot` construit et vérifié de bout en bout en configuration Release (322/322 tests, garde-fous statiques Phase 3 et Lot 2 tous verts) — commit `0b589c3`.
  - `GameSaveHub-Client-Lot2-0.4.0-win-x64.zip` — `sha256:5caafcad6df5baebd3b7bf1ab6bb00615a18a5b137c83e2586329d0b454a60b`
  - `GameSaveHub-Client-Lot2-0.4.0-PILOTE-win-x64.zip` — `sha256:f2b115cf063bfea90a2c3b1c5cbc5601a014be96ba39b4b8307f4d4fd3f3ce7`

### Parcours quotidien (hérité de l'ancien pilote, toujours à revérifier après ces changements)

- [ ] Charger réellement le monde importé, modifier un élément identifiable, sauvegarder, fermer et vérifier la progression publiée de bout en bout.
- [ ] Vérifier un redémarrage Windows pendant une session `InGame`, puis la reprise de capture après stabilité.
- [ ] Vérifier une coupure contrôlée pendant l'import et la réconciliation sans nouvelle écriture ambiguë.
- [ ] Vérifier les états contextuels : jeu lancé hors Hub, serveur indisponible puis rétabli, hôte distant en préparation et en jeu, reprise manuelle après incident, mise à jour requise.
- [ ] Vérifier l'interface à 1440×1024 et aux échelles Windows 100 %, 125 %, 150 % et 200 %.
- [ ] Vérifier clavier seul, focus visible, noms lecteur d'écran, retour `Copié`, absence de débordement et absence d'identifiants techniques sur l'accueil.

## Reste à faire avant la validation réelle

- [ ] Tâche 11 — Vérification automatisée complète : reconstruction propre, revue ciblée (sélection par nom affiché, contournement du verrou de transition, écriture WGS sans snapshot, fuite d'identifiant technique vers l'IHM), `git diff --check`, régénération et contrôle de `SOURCE-SHA256SUMS.txt`.

## Acceptation réelle du slot permanent

- [ ] Créer un nouveau snapshot WGS et une sauvegarde de `ProgramData` juste avant migration.
- [ ] Installer `0.4.0-pilot` sur Steven après approbation explicite.
- [ ] Rattacher l'unique `GSH-SHLAGS-RETURN`, puis le renommer en `GSH-MONDE-PARTAGE` uniquement lors de l'import sûr suivant.
- [ ] Première prise en main : même nom logique, aucun monde supplémentaire, vraie modification de jeu publiée, monde serveur de nouveau disponible.
- [ ] Deuxième prise en main : même nom logique réutilisé et ensemble des mondes WGS inchangé.
- [ ] Vérifier redémarrage, interruption d'import, reprise idempotente et `maintenance-status`.
- [ ] Confirmer qu'aucune suppression automatique n'a eu lieu et que `Shlags1` et `Standard-1` sont inchangés.

## Compatibilité Lot 3 à préserver

- [x] `managed-slot.json` séparé de `client-state.json`, propre schéma, écritures atomiques (implémenté, non encore éprouvé en réinstallation réelle).
- [ ] L'updater bloque toute activation pendant configuration, rattachement, migration, réparation, inspection ou écriture du slot (dépend de l'updater du Lot 3, non commencé).
- [ ] Le contrôle de santé de l'updater ne déclenche aucune migration ou écriture WGS (`maintenance-status` est déjà strictement en lecture seule ; reste à brancher un vrai updater).
- [ ] `managed-slot.json` survit à la réinstallation et à la mise à jour ; il ne contient aucun secret (à prouver sur un vrai PC).
- [x] API additive déployée avec version minimale non contraignante par défaut.
- [ ] Ne relever `MinimumAcquireVersion` qu'après validation du même paquet sur Steven et Bob (Tâche 14, étape 5).
- [ ] Après migration réelle, n'autoriser le rollback automatique que vers une version comprenant le slot permanent.
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

Le Lot 2 n'est terminé que lorsque toutes les cases « Acceptation réelle du slot permanent » et les validations Bob sont cochées avec preuves, que le total automatisé reste au moins égal à 322 tests sans échec, et que l'utilisateur a validé le résultat. Le Lot 3 peut alors utiliser `0.4.0-pilot` comme socle manuel compatible, sans revenir à une version susceptible de recréer des placeholders.
