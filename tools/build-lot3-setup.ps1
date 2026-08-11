$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot "artifacts"
$version = "0.5.0"
$publishRoot = Join-Path $artifactRoot "GameSaveHub-Setup-$version"

# La version est écrite à deux endroits qu'aucun compilateur ne relie : ici, et dans la
# constante SetupPaths.CurrentVersion embarquée dans l'exécutable (valeur de repli quand le
# payload ne porte pas de fichier VERSION). Une dérive silencieuse entre les deux ferait
# installer un numéro de version faux, donc mal comparer à la prochaine mise à jour : on la
# refuse ici plutôt que de la découvrir sur le PC d'un joueur.
$setupPathsSource = Get-Content -LiteralPath (Join-Path $repoRoot "src\GameSaveHub.Client.Setup\SetupPaths.cs") -Raw
if ($setupPathsSource -notmatch 'CurrentVersion\s*=\s*"([^"]+)"') {
    throw "Constante SetupPaths.CurrentVersion introuvable : le contrôle de cohérence de version ne peut plus être fait."
}
if ($Matches[1] -ne $version) {
    throw "Version incohérente : ce script publie $version, SetupPaths.CurrentVersion vaut $($Matches[1])."
}

if (Test-Path -LiteralPath $publishRoot) { Remove-Item -LiteralPath $publishRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

Write-Host "Publication du service et de l'application (payload embarqué)..."
dotnet publish (Join-Path $repoRoot "src\GameSaveHub.Client.Service\GameSaveHub.Client.Service.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o (Join-Path $publishRoot "payload\Service")
dotnet publish (Join-Path $repoRoot "src\GameSaveHub.Client.App\GameSaveHub.Client.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o (Join-Path $publishRoot "payload\App")
Set-Content -LiteralPath (Join-Path $publishRoot "payload\VERSION") -Value $version -NoNewline
Set-Content -LiteralPath (Join-Path $publishRoot "VERSION") -Value $version -NoNewline

Write-Host "Publication de GameSaveHub-Setup.exe (single-file)..."
dotnet publish (Join-Path $repoRoot "src\GameSaveHub.Client.Setup\GameSaveHub.Client.Setup.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o (Join-Path $publishRoot "setup-exe")

Copy-Item -LiteralPath (Join-Path $publishRoot "setup-exe\GameSaveHub-Setup.exe") -Destination $publishRoot -Force

# Paquet de mise à jour. Sa racine doit être exactement ce que contient
# %ProgramFiles%\GameSaveHub\Client après installation — Service\, App\, VERSION — parce que
# l'updater extrait ce zip puis renomme le dossier obtenu en Client : un niveau de dossier
# supplémentaire produirait une installation vide. C'est précisément la disposition du
# dossier payload\, qui est donc compressé tel quel : le contenu installé et le contenu
# livré par mise à jour sont ainsi le même arbre, par construction.
$payloadRoot = Join-Path $publishRoot "payload"
$packagePath = Join-Path $artifactRoot "GameSaveHub-Client-$version.zip"
if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
Write-Host "Assemblage du paquet de mise à jour..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $payloadRoot,
    $packagePath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

$exeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $publishRoot "GameSaveHub-Setup.exe")).Hash
$packageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash
Write-Host ""
Write-Host "Paquet prêt : $publishRoot"
Write-Host "SHA-256 de GameSaveHub-Setup.exe : $exeHash"
Write-Host "Paquet de mise à jour : $packagePath"
Write-Host "SHA-256 du paquet : $packageHash"
Write-Host ""
Write-Host "Étape suivante (poste de Steven, clé privée locale) :"
Write-Host "  dotnet run --project src\GameSaveHub.Server.Admin -- client-release sign $packagePath $version <cle-privee.pem>"
