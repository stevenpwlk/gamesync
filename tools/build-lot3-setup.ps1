$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot "artifacts"
$version = "0.5.0"
$publishRoot = Join-Path $artifactRoot "GameSaveHub-Setup-$version"

if (Test-Path -LiteralPath $publishRoot) { Remove-Item -LiteralPath $publishRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

Write-Host "Publication du service et de l'application (payload embarqué)..."
dotnet publish (Join-Path $repoRoot "src\GameSaveHub.Client.Service\GameSaveHub.Client.Service.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o (Join-Path $publishRoot "payload\Service")
dotnet publish (Join-Path $repoRoot "src\GameSaveHub.Client.App\GameSaveHub.Client.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o (Join-Path $publishRoot "payload\App")
Set-Content -LiteralPath (Join-Path $publishRoot "payload\App\..\VERSION") -Value $version -NoNewline
Set-Content -LiteralPath (Join-Path $publishRoot "VERSION") -Value $version -NoNewline

Write-Host "Publication de GameSaveHub-Setup.exe (single-file)..."
dotnet publish (Join-Path $repoRoot "src\GameSaveHub.Client.Setup\GameSaveHub.Client.Setup.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o (Join-Path $publishRoot "setup-exe")

Copy-Item -LiteralPath (Join-Path $publishRoot "setup-exe\GameSaveHub-Setup.exe") -Destination $publishRoot -Force

$exeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $publishRoot "GameSaveHub-Setup.exe")).Hash
Write-Host ""
Write-Host "Paquet prêt : $publishRoot"
Write-Host "SHA-256 de GameSaveHub-Setup.exe : $exeHash"
