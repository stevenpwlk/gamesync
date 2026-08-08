# Architecture — état du dépôt

## Frontière du jalon actuel

Le dépôt contient les contrats génériques, les primitives de sécurité et l'adaptateur Planet Crafter. Les expériences cross-PC d'août 2026 ont permis d'implémenter la préparation d'hôte et l'import ciblé dans l'adaptateur, tout en conservant le feature gate serveur fermé pour la production générale. Les preuves et invariants sont consignés dans `docs/investigation/CROSS-PC-VALIDATION-2026-08.md`.

```text
Diagnostics
  → IGameSaveAdapter
    → PlanetCrafterGamePassAdapter
      → détection package/processus
      → inventaire WGS en lecture seule
      → snapshot cohérent sans écriture source
```

`AdapterCapabilityReport` expose désormais la préparation d'hôte et l'import ciblé comme capacités techniques disponibles pour le pilote. Le lancement automatisé reste désactivé. Le serveur conserve `GSH_ALLOW_HOST_TRANSFER=false` tant que le jalon opérationnel complet n'est pas atteint.

La préparation d'hôte refuse tout pseudo absent ou ambigu et transforme uniquement les IDs joueur / flags hôte nécessaires. L'import exige une baseline complète créée avant le placeholder et ne peut écrire que dans l'unique nouveau `Standard-X`. Le format portable reste décrit dans `docs/investigation/ARTIFACT-FORMAT.md`.

## Invariants déjà imposés

- aucune copie de métadonnées WGS entre PC ;
- snapshot uniquement jeu fermé et avec reconnaissance explicite d'un monde test;
- refus des points de réanalyse et chemins sortant de la racine;
- aucun écrasement automatique d'un rapport ou snapshot;
- hash SHA-256 avant/après copie et rejet si la source change;
- aucune donnée de sauvegarde affichée, seulement métadonnées et hashes.


## Invariants pilote ajoutés après validation cross-PC

- un utilisateur ne peut préparer une sauvegarde que si son pseudo existe déjà dans l'artefact ;
- le joueur local est le joueur ID 0 ; le flag `host` seul n'est pas utilisé pour sélectionner un personnage ;
- la préparation conserve inventaires, équipements, positions et données du personnage ;
- l'import part d'une baseline WGS complète et protège tous les mondes existants par hash ;
- seul l'unique nouveau `Standard-X` créé après la baseline peut être ciblé ;
- snapshot pré-import + rollback immédiat en cas d'échec après écriture.
