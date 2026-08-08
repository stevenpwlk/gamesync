# GameSave Hub Probe

`GameSaveHub.Probe.exe` est le programme à remettre au propriétaire du deuxième PC. Il est autonome pour Windows 11 x64 et ne requiert ni .NET ni droits administrateur.

## Garanties actuelles

- inventaire WGS en lecture seule ;
- détection de l'installation, de la version, du jeu actif, des mondes et des joueurs ;
- vérification de stabilité pendant la lecture ;
- aucune donnée envoyée automatiquement ;
- création volontaire d'un fichier `.gshdiag` ;
- inclusion facultative d'un seul monde explicitement déclaré jetable ;
- refus d'inclure une sauvegarde si le jeu tourne ou si les fichiers sont instables.

Le rapport n'inclut pas les chemins absolus d'installation. Les chemins WGS internes sont relatifs. Le monde optionnel contient cependant de vraies données de sauvegarde et doit être traité comme privé.

## Mode opératoire à envoyer à l'ami

1. Fermer The Planet Crafter.
2. Télécharger `GameSaveHub.Probe.exe`.
3. Vérifier le SHA-256 communiqué séparément.
4. Lancer l'exécutable. Windows SmartScreen peut afficher un avertissement, car la V1 n'a pas de certificat Authenticode.
5. Cliquer sur **Analyser ce PC**.
6. Sélectionner uniquement un monde réellement jetable. Cocher l'inclusion seulement après confirmation.
7. Cliquer sur **Créer le rapport** et transmettre uniquement le fichier `.gshdiag` produit.

Ne jamais lui demander d'éteindre Internet, de remplacer WGS ou de tester l'import avant analyse de ce rapport et préparation d'un protocole interactif dédié.

## Build reproductible

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' publish `
  '.\src\GameSaveHub.Client.Probe\GameSaveHub.Client.Probe.csproj' `
  --configuration Release --runtime win-x64 `
  --output '.\artifacts\GameSaveHub-Probe-0.1.0-win-x64'
```
