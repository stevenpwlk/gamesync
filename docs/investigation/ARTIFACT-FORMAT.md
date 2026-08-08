# Format d'artefact GameSave Hub v1

Extension : `.gshsave`

L'artefact est une archive ZIP non compressée contenant exactement deux entrées :

```text
manifest.json
payload/world.save
```

Il ne contient jamais `containers.index`, un fichier `container.*`, un identifiant physique de blob WGS, `PlayerPrefs.json`, les succès ou un chemin absolu du PC source.

## Manifeste

Le manifeste version 1 contient :

- l'identifiant d'adaptateur `planet-crafter-pc-gamepass`;
- le nom logique (`Standard-2.json`) et le nom affiché (`Shlags1`);
- planète, mode et seed du monde;
- date UTC de capture;
- chemin, taille et SHA-256 du payload;
- joueurs, indicateur d'hôte et identifiants d'inventaire/équipement.

## Validation

La validation refuse :

- toute entrée supplémentaire ou tout chemin différent des deux chemins autorisés;
- un manifeste supérieur à 64 Kio;
- un monde vide ou supérieur à 256 Mio;
- un ratio de compression supérieur à 100 (`ArtifactEnvelopeValidator.MaximumCompressionRatio`);
- une taille ou un SHA-256 différent du manifeste;
- un format de monde illisible;
- un nom logique, nom affiché, seed ou tableau de joueurs différent du contenu réel.

Le serveur pourra traiter l'enveloppe et ses hashes sans connaître WGS. L'adaptateur client reste responsable de la validation sémantique et de l'injection ciblée dans le conteneur local courant.

## Artefact de référence

- Monde : `Shlags1` / `Standard-2.json`
- Fichier : `20260802T223655Z-1f296268d7004b41ae98d11509a8cee2.gshsave`
- Taille : 31 648 octets
- SHA-256 : `6f1206a700f6995f906e4f1b947bede43d3cae1a004790639c9f22e96f9d69ea`


## Préparation d'hôte

Un artefact préparé conserve la même enveloppe v1. Seuls le payload et le tableau `players` du manifeste changent.

La préparation est autorisée uniquement si le pseudo cible existe exactement une fois et si la topologie joueur est saine. Le joueur cible reçoit l'ID 0 et devient l'unique hôte ; l'ancien ID 0 reçoit l'ancien ID du joueur cible. Les `inventoryId`, `equipmentId`, positions et autres champs de personnage ne sont pas réassignés.

Si le pseudo cible est absent, GameSave Hub refuse explicitement l'opération : il ne crée pas un nouveau personnage et ne réutilise pas silencieusement le slot d'un autre joueur.
