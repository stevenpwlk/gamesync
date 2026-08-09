param(
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$repo = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repo 'artifacts'
$packageName = "GameSaveHub-SaveExporter-$Version-win-x64"
$packageDirectory = Join-Path $artifactRoot $packageName
$publishDirectory = Join-Path $packageDirectory 'Exporter'
$zipPath = Join-Path $artifactRoot ($packageName + '.zip')
$hashPath = $zipPath + '.sha256'

$sdkVersion = (& dotnet --version).Trim()
if (-not $sdkVersion.StartsWith('10.')) {
    throw "SDK .NET 10 requis. Version detectee : $sdkVersion"
}

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $hashPath) {
    Remove-Item -LiteralPath $hashPath -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

$testArguments = @(
    'test',
    (Join-Path $repo 'tests\Unit\GameSaveHub.UnitTests.csproj'),
    '--configuration', 'Release',
    '--filter', 'SaveExporterServiceTests'
)
& dotnet @testArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Les tests du mini-exporteur ont echoue.'
}

$publishArguments = @(
    'publish',
    (Join-Path $repo 'src\GameSaveHub.SaveExporter\GameSaveHub.SaveExporter.csproj'),
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    "-p:Version=$Version",
    '--output', $publishDirectory
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw 'La publication du mini-exporteur a echoue.'
}

$executable = Join-Path $publishDirectory 'GameSaveHub.SaveExporter.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Executable mono-fichier absent.'
}

$smokeProcess = Start-Process -FilePath $executable -PassThru
try {
    if (-not $smokeProcess.WaitForExit(8000)) {
        Write-Host 'Smoke test UI : la fenetre reste active.'
    }
    else {
        throw "Smoke test UI echoue : sortie prematuree (code $($smokeProcess.ExitCode))."
    }
}
finally {
    if (-not $smokeProcess.HasExited) {
        Stop-Process -Id $smokeProcess.Id
    }
}

$readme = @'
GAME SAVE HUB - EXPORTER UNE SAUVEGARDE

1. Fermez completement The Planet Crafter.
2. Lancez GameSaveHub.SaveExporter.exe.
3. Choisissez la sauvegarde grace aux joueurs et a la date.
4. Cliquez sur "Exporter cette sauvegarde".
5. Envoyez uniquement le fichier .gshsave genere a Steven.

Le programme ne modifie jamais les sauvegardes du jeu.
'@
Set-Content -LiteralPath (Join-Path $packageDirectory 'LISEZ-MOI.txt') -Encoding ASCII -Value $readme

Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hashPath -Encoding ASCII -Value "$hash *$(Split-Path -Leaf $zipPath)"

Write-Host "ZIP     : $zipPath"
Write-Host "SHA-256 : $hash"
