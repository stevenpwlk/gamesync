# Preuves expérimentales

Ces fichiers sont les **preuves scientifiques du projet**. Ils ne sont pas régénérables : ils documentent des expériences réelles menées sur deux PC physiques avec deux comptes Xbox différents, les 6 et 7 août 2026.

Les rapports `.gsh*diag` sont des archives ZIP contenant un `manifest.json` et les données de session. Ils peuvent être ouverts avec n'importe quel outil ZIP.

**Ne jamais purger ce dossier.**

## Rapports de diagnostic

| Fichier | Machine | Date UTC | Type | Issue |
|---|---|---|---|---|
| `…HostSelectionTest-PC-STEVEN-20260807-085644-628b07.gshhostdiag` | `PC-STEVEN` | 07/08 06:56 | `host-selection-safety-test` v0.1.0 | Cible `Standard-5.json` / `GSHXFER67CC35` |
| `…OnlineCloudTest-PC-STEVEN-20260807-114249-023b09.gshclouddiag` | `PC-STEVEN` | 07/08 09:42 | `online-cloud-safety-test` v0.1.1 | Cible `Standard-6.json` / `GSHONLINE5FB4BF` |
| `…BobOnlineSafe-BOBXIME-20260807-134328-fa810a.gshbobdiag` | `BOBXIME` (`maxim`) | 07/08 11:43 | `bob-online-safe` v0.2.1 | `SuccessSyncUnclear`, monde importé conservé |
| `…StevenRoundTripSuite-PC-STEVEN-20260807-142410-64e66a.gshroundtripdiag` | `PC-STEVEN` | 07/08 12:24 | `steven-roundtrip-suite` v0.1.0 | **`SuccessSynchronized`** |
| `…NetworkAuthProbe-PC-STEVEN-20260807-170441.gshnetdiag` | `PC-STEVEN` | 07/08 17:04 | Sonde réseau/auth v0.1.4 | Enrôlement + challenge ECDSA + JWT validés |

## Ce que chaque rapport démontre

**`gshhostdiag` — sélection de l'hôte.** C'est le rapport qui a produit la découverte centrale du projet : le joueur incarné localement est celui dont `id == 0`, pas celui marqué `host=true`. Le jeu réaffirme `host=true` sur l'ID 0 à la sauvegarde suivante, et peut réécrire le champ `name` avec l'identité Xbox locale. Toute la logique `prepare-host` (échange d'IDs joueur) découle de ce constat.

**`gshclouddiag` — Xbox Cloud en ligne.** Un placeholder créé normalement par le jeu, synchronisé, puis remplacé localement jeu fermé et **Internet actif**, surveillé 20 secondes, rechargé, joué, sauvegardé, puis observé `Synchronisé` dans l'application Xbox. Aucun rollback cloud ni conflit visible. Preuve empirique du parcours en ligne — pas une preuve cryptographique du contenu distant Microsoft.

**`gshbobdiag` — exécution sur le PC distant.** Même opération sur la machine de l'ami, avec un compte Xbox différent. L'issue `SuccessSyncUnclear` signale que l'opération a réussi mais que l'état de synchronisation cloud n'a pas pu être affirmé avec certitude au moment de la capture.

**`gshroundtripdiag` — l'aller-retour complet.** Le monde `Shlags1` a effectué le cycle `Steven ID0 → Bob ID0 → Steven ID0` avec conservation des inventaires, équipements, positions, autres joueurs et progression du monde. C'est la preuve de faisabilité du produit.

**`gshnetdiag` — authentification NAS.** Enrôlement par code d'invitation, création de clé ECDSA P-256, challenge signé et obtention d'un JWT contre `saves.stevenpwlk.fr:18443`. Le `DeviceId` utilisé (`bf7e13ed-0ad1-4aca-8ee2-8cd4d3826991`) est un appareil temporaire à révoquer — voir `docs/operations/CLEANUP-TEST-ASSETS-2026-08-07.md`.

## Artefact canonique

`SHLAGS1-CANONICAL-ROUNDTRIP-20260807.gshsave` — 31 668 octets

```text
SHA-256 : 30af9efca4bed6b7042c7dae4f83fedaa8fc9311c22153735d3a00fc96d76495
```

C'est le monde `Shlags1` **après** l'aller-retour validé du 7 août. C'est cet artefact que le palier C du protocole de déploiement importe sur le NAS comme version initiale du monde pilote.

Joueurs sérialisés :

| Pseudo | ID | Hôte | Inventaire | Équipement |
|---|---|---|---|---|
| `Stevenpwlk` | 0 | oui | 3 | 4 |
| `Maxdrake59` | 4 | non | 7 | 8 |
| `BoB XiMe` | 7 | non | 5 | 6 |

## Preuves conservées hors Git

Volumineuses, mais tout aussi irremplaçables — elles restent sur disque, ignorées par Git :

- `snapshots/` — 7 captures WGS cohérentes du 2 août, dont le snapshot pré-restauration de l'essai hors ligne ;
- `artifacts/transfer-20260806-181352/` — inventaires avant/après, comparaison et artefact d'un transfert du 6 août ;
- `diagnostics-output/` — inventaires JSON et rapport de l'essai de restauration hors ligne du 3 août.
