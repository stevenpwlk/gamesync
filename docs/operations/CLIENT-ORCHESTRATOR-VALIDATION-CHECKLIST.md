# Checklist de validation — GameSave Hub V1

**Dernière mise à jour :** 10 août 2026

**Branche de validation :** `codex/v1-lot2-contextual-app`

**État global :** Lot 1 fusionné sur `main`. Lot 2 (slot local permanent) entièrement implémenté, testé automatiquement (322 tests, 0 échec) et validé réellement de bout en bout ce soir, avec Bob : installation, configuration initiale, cycle `Steven → Bob → Steven`, un vrai incident de reprise, et le parcours symétrique dans les deux sens (Bob héberge/Steven rejoint, puis Steven héberge/Bob rejoint) — tous réussis. En bonus, le monde partagé réel a été remplacé en production par une nouvelle sauvegarde reçue de Bob (voir [`REPLACE-PRIMARY-WORLD.md`](REPLACE-PRIMARY-WORLD.md)), et un second aller-retour complet a confirmé que tout fonctionne avec ce nouveau contenu. Reste avant clôture formelle : le redémarrage Windows / coupure de service en session `InGame` (seul point de la section « Acceptation réelle » encore ouvert), une dernière revue automatisée, et la validation explicite de l'utilisateur pour la fusion vers `main`.

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

- [x] Charger réellement le monde importé, modifier un élément identifiable, sauvegarder, fermer et vérifier la progression publiée de bout en bout. (2026-08-10 — deux fois, voir « Acceptation réelle du slot permanent » ci-dessus.)
- [ ] Vérifier un redémarrage Windows pendant une session `InGame`, puis la reprise de capture après stabilité. (regroupé avec la coupure de service ci-dessous, à faire ensemble.)
- [ ] Vérifier une coupure contrôlée pendant l'import et la réconciliation sans nouvelle écriture ambiguë. (regroupé avec le redémarrage Windows — la réutilisation étant désormais quasi instantanée, ce test cible plutôt la coupure du service pendant une session `InGame` en cours.)
- [x] État contextuel « jeu lancé hors Hub ». (2026-08-10 — jeu lancé directement depuis Xbox sans passer par GameSave Hub ; accueil affiche bien « Le jeu est lancé hors de GameSave Hub », aucune action ; confirmé côté service : `gameRunning=true`, aucune session locale ni distante.)
- [x] État contextuel « hôte distant en préparation et en jeu ». (2026-08-10 — vrai pilote avec Bob : écran de Steven affichait correctement « BoB XiMe prépare le monde » pendant que Bob configurait/hébergeait, rejoint sans problème ensuite.)
- [x] État contextuel « reprise manuelle après incident ». (2026-08-10 — incident réel non planifié pendant le pilote avec Bob, voir section dédiée ci-dessus.)
- [ ] États contextuels restants : serveur indisponible puis rétabli, mise à jour requise.
- [ ] Vérifier l'interface à 1440×1024 et aux échelles Windows 100 %, 125 %, 150 % et 200 %. (reporté à la demande de l'utilisateur.)
- [ ] Vérifier clavier seul, focus visible, noms lecteur d'écran, retour `Copié`, absence de débordement et absence d'identifiants techniques sur l'accueil. (jugé non utile par l'utilisateur pour l'instant.)

## Reste à faire avant la validation réelle

- [x] Tâche 11 — Vérification automatisée complète : reconstruction propre, revue ciblée (sélection par nom affiché, contournement du verrou de transition, écriture WGS sans snapshot, fuite d'identifiant technique vers l'IHM), `git diff --check`, régénération et contrôle de `SOURCE-SHA256SUMS.txt`. (2026-08-10, commit `dc7798a`)

## Bugs trouvés et corrigés pendant le pilote réel (2026-08-10)

1. Après un rattachement réussi, `ManagedSlotResolver` renvoie `RenamePending` (slot lié, nom affiché WGS pas encore `GSH-MONDE-PARTAGE`). Ni l'accueil ni `transfer-start` ne géraient ce statut : les deux tombaient dans le repli générique « à vérifier », bloquant définitivement l'étape qui devait justement effectuer le renommage. Corrigé en traitant `RenamePending` comme `Ready` aux deux endroits (commit `353c5d1`).
2. La deuxième prise en main échouait avec `managed_slot_baseline_failed` : `managed-slot.json` garde le nom affiché *d'origine* (`GSH-SHLAGS-RETURN`) et n'était jamais mis à jour après un remplacement réussi, donc la baseline de la deuxième réutilisation comparait le nom affiché réel (déjà renommé) au nom périmé du binding. Aucune écriture WGS n'avait eu lieu (refus avant `serverImportStarted`). Corrigé en synchronisant `managed-slot.json` après chaque remplacement réussi (commit `f448b26`) ; le double de test qui avait laissé passer ce défaut a aussi été durci pour reproduire fidèlement la vérification du vrai adaptateur. La première réutilisation réelle avait eu lieu *avant* ce correctif : `managed-slot.json` restait donc bloqué sur l'ancien nom même après réinstallation (le correctif ne synchronise qu'après une écriture réussie, et l'état périmé bloquait justement cette écriture). Corrigé une fois manuellement (édition du seul champ `currentDisplayName`, aucune écriture WGS, exécutée en PowerShell administrateur après accord explicite) pour repartir d'un état cohérent ; confirmé ensuite que le service resynchronise correctement tout seul sans intervention.

Paquet `0.4.0-pilot` reconstruit et réinstallé après chaque correctif ; les deux confirmés en conditions réelles.

## Acceptation réelle du slot permanent

- [x] Créer un nouveau snapshot WGS et une sauvegarde de `ProgramData` juste avant migration. (2026-08-10 — snapshot `snapshots/20260810T090702Z-3ee7278f96d447a19f87bf317df96f59`, 9 fichiers vérifiés ; sauvegarde `ProgramData` dans `snapshots/pre-migration-programdata-20260810T090702Z/`, 81 fichiers, 1.14 Mo.)
- [x] Installer `0.4.0-pilot` sur Steven après approbation explicite. (2026-08-10 — service `Running`/`Automatic`, pipe répond en 1.7 s, `deviceId`/pseudo/clé CNG préservés, pas de ré-enrôlement. Réinstallé une deuxième fois après le correctif `RenamePending`.)
- [x] Rattacher l'unique `GSH-SHLAGS-RETURN`. (2026-08-10 — `managed-slot.json` créé, `logicalName: Standard-5.json`, `desiredDisplayName: GSH-MONDE-PARTAGE` ; comparaison rigoureuse des 9 fichiers WGS avant/après rattachement : hash strictement identiques, aucun octet modifié.)
- [x] Renommer en `GSH-MONDE-PARTAGE` lors de l'import sûr suivant. (2026-08-10 — `targetDisplayName` confirmé `GSH-MONDE-PARTAGE` après la première prise en main ; visible dans le jeu.)
- [x] Première prise en main : même nom logique, aucun monde supplémentaire, vraie modification de jeu publiée, monde serveur de nouveau disponible. (2026-08-10 — session `Completed` sans erreur, `resultVersionId` du client identique au `currentVersionId` serveur, monde `Available`, dernière activité `Stevenpwlk` horodatée ; toujours exactement 3 mondes WGS, aucun `Standard-6.json` ; diff complet des 9 fichiers WGS avant/après confiné aux deux groupes de conteneurs déjà propres à `Standard-5.json`.)
- [x] Deuxième prise en main : même nom logique réutilisé et ensemble des mondes WGS inchangé. (2026-08-10 — session `Completed` sans erreur, `resultVersionId` de nouveau identique au `currentVersionId` serveur ; toujours exactement 3 mondes WGS (`Standard-1`, `Shlags1`, `GSH-MONDE-PARTAGE`/`Standard-5.json`), aucun `Standard-6.json` ; `managed-slot.json` resté correctement synchronisé sans intervention manuelle cette fois — confirme que le correctif `f448b26` s'auto-entretient une fois reparti d'un état cohérent.)
- [ ] Vérifier redémarrage, interruption d'import, reprise idempotente et `maintenance-status`.
- [x] Confirmer qu'aucune suppression automatique n'a eu lieu et que `Shlags1` et `Standard-1` sont inchangés. (2026-08-10 — mêmes 3 mondes après les deux prises en main réelles ; toute l'activité de fichiers WGS observée reste confinée au groupe de conteneurs de `Standard-5.json`.)

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

- [x] Bob est disponible. (2026-08-10, soir.)
- [x] Installer sur Bob exactement le même ZIP `0.4.0-pilot` (`sha256:b992caa5e...874572`). (2026-08-10 — installation et enrôlement réussis sans problème signalé.)
- [x] Réaliser sa configuration unique avec un seul `GSH-MONDE-PARTAGE`. (2026-08-10 — réussie sans problème signalé.)
- [x] Réaliser le cycle réel `Steven → Bob → Steven` avec progression vérifiée à chaque publication. (2026-08-10 — cycle complet : Bob a pris la main en premier (configuration initiale = première prise en main), Steven a rejoint sa partie sans problème. Bob a sauvegardé et fermé le jeu, puis a subi une **vraie interruption non planifiée** pendant la capture/publication (session locale passée en `Interrupted`, aucune écriture supplémentaire) ; il a cliqué **« Reprendre en sécurité »** avec succès, monde de nouveau `Available`, activité `BoB XiMe` publiée et confirmée côté serveur. Steven a ensuite pris la main à son tour : session `Completed` sans erreur, `resultVersionId` identique au `currentVersionId` serveur, activité `Stevenpwlk` publiée après celle de Bob. **Confirmé par Steven : la modification faite par Bob dans le jeu était bien présente** à la reprise en main — la progression a réellement traversé le cycle, pas seulement les métadonnées de publication. Toujours exactement 3 mondes sur le PC de Steven (`Standard-1`, `Shlags1`, `GSH-MONDE-PARTAGE`), aucun `Standard-6.json`.)
- [x] Vérifier le parcours de connexion pendant que Bob héberge. (2026-08-10 — écran de Steven affichait correctement « BoB XiMe prépare le monde » puis l'état d'hébergement distant ; rejoint sans problème.)
- [x] Vérifier le parcours symétrique (Steven héberge, Bob rejoint). (2026-08-10 — Steven a hébergé, Bob a rejoint, Steven a sauvegardé/fermé/publié ; puis l'inverse immédiatement après (Bob héberge à nouveau). Les deux sens réussis sans problème signalé, avec en plus la nouvelle sauvegarde de Bob comme contenu de départ.)
- [ ] Obtenir un accord explicite avant tout nouveau déploiement NAS/API, migration de production, écriture WGS de validation ou élévation de version minimale.
- [ ] Exécuter la revue finale, les tests complets et les sauvegardes finales.
- [ ] Obtenir la validation conjointe avant fusion vers `main` ; aucun push ou merge avant cet accord.

## Reprise après incident — validée en conditions réelles (2026-08-10)

Non planifiée : pendant le tout premier pilote réel avec Bob, sa session locale a été interrompue après sauvegarde/fermeture du jeu, pendant la sécurisation (capture ou publication) — la cause exacte n'a pas été creusée puisque la reprise a réglé la situation, mais elle est consignée dans son diagnostic local si besoin d'y revenir. Chez Bob : état `Interrupted`, action unique proposée « Reprendre en sécurité », aucune reprise automatique tentée. Chez Steven : le monde restait affiché « en préparation », cohérent avec une session distante non terminée, pas une anomalie séparée. Après le clic de Bob : reprise réussie sans nouvelle écriture ambiguë, publication aboutie, monde `Available`. Ceci couvre la ligne « reprise manuelle après incident » des états contextuels ci-dessus, désormais validée.

## Critère de clôture

Le Lot 2 n'est terminé que lorsque toutes les cases « Acceptation réelle du slot permanent » et les validations Bob sont cochées avec preuves, que le total automatisé reste au moins égal à 322 tests sans échec, et que l'utilisateur a validé le résultat. Le Lot 3 peut alors utiliser `0.4.0-pilot` comme socle manuel compatible, sans revenir à une version susceptible de recréer des placeholders.
